#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Elts.Operator
{
    public sealed partial class DevelopmentSessionPanel
    {
        private readonly SemaphoreSlim collectionGate=new SemaphoreSlim(1,1);
        private ParticipantCollection? collection;
        private Task? collectionOperation;
        private object collectionProfiles=Array.Empty<object>(), collectionRows=Array.Empty<object>();
        private object? collectionProfile;
        private string collectionMessage="Preparing local storage…", collectionError="", collectionExport="";
        private bool collectionReady;
        private string collectionRowsSession="",collectionRowsStream="";
        private int collectionRowsPage;
        public string CollectionExportPath=>collectionExport;
        public object CollectionState=>new { ready=collectionReady,busy=collectionOperation!=null && !collectionOperation.IsCompleted,
            message=collectionMessage,error=collectionError,path=collection?.Root??"",profiles=collectionProfiles,profile=collectionProfile,rows=collectionRows,rowsSession=collectionRowsSession,rowsStream=collectionRowsStream,rowsPage=collectionRowsPage };

        public void InitializeCollection()
        {
            collection=new ParticipantCollection(StationDataRoot);
            CollectionAction(new JObject { ["action"]="refreshData" });
        }
        public void CollectionAction(JObject command)
        {
            if(collectionOperation!=null && !collectionOperation.IsCompleted)throw new InvalidOperationException("Data is still loading. Please wait.");
            collection??=new ParticipantCollection(StationDataRoot);
            collectionOperation=RunCollectionAction(command);
        }
        private async Task RunCollectionAction(JObject command)
        {
            collectionMessage="Working…";collectionError="";
            await collectionGate.WaitAsync();
            try
            {
                string action=(string?)command["action"]??"";
                await Task.Run(()=>collection!.Initialize());collectionReady=true;
                if(action=="refreshData")
                {
                    string warning=await Task.Run(()=>ImportCollectionFolders());
                    collectionProfiles=await Task.Run(()=>collection!.Profiles());
                    string selected=(string?)command["id"]??"";
                    if(selected.Length>0)collectionProfile=await Task.Run(()=>collection!.Profile(selected));
                    collectionError=warning;collectionMessage="Local collection refreshed.";
                }
                else if(action=="viewParticipant")
                {
                    collectionProfile=await Task.Run(()=>collection!.Profile((string?)command["id"]??""));
                    collectionRows=Array.Empty<object>();collectionMessage="Participant profile loaded.";
                }
                else if(action=="viewDataRows")
                {
                    collectionRowsSession=(string?)command["session"]??"";collectionRowsStream=(string?)command["stream"]??"events.ndjson";collectionRowsPage=(int?)command["page"]??0;
                    collectionRows=await Task.Run(()=>collection!.Records(collectionRowsSession,collectionRowsStream,collectionRowsPage));
                    collectionMessage="Original records loaded · "+ParticipantCollection.RecordsPerPage+" per page.";
                }
                else if(action=="removeParticipant" || action=="removeRun")
                {
                    if((bool?)command["confirmed"]!=true)throw new ArgumentException("Confirm removal before deleting data.");
                    string id=(string?)command["id"]??"";
                    string activeDirectory=recording?.IsOpen==true?recording.RunDirectory:"";
                    string activeCode=recording?.IsOpen==true?stationParticipant:"";
                    await Task.Run(()=>collection!.Remove(id,action=="removeParticipant",activeDirectory,activeCode));
                    collectionProfiles=await Task.Run(()=>collection!.Profiles());
                    string selected=(string?)command["participant"]??"";
                    collectionProfile=action=="removeRun" && selected.Length>0?await Task.Run(()=>collection!.Profile(selected)):null;
                    collectionRows=Array.Empty<object>();collectionRowsSession="";collectionRowsStream="";collectionRowsPage=0;
                    collectionExport="";
                    collectionMessage="Data and names removed from the viewer. Original recording files and existing exports are retained.";
                }
                else if(action=="exportSessionRaw")
                {
                    collectionExport="";
                    string sessionId=(string?)command["id"]??"";
                    string directory=await Task.Run(()=>collection!.SessionDirectory(sessionId));
                    if(recording?.IsOpen==true && String.Equals(recording.RunDirectory,directory,StringComparison.OrdinalIgnoreCase))throw new IOException("Finish this recording before exporting its original files.");
                    if(!File.Exists(Path.Combine(directory,"session-summary.json")))throw new IOException("This recording is incomplete. Retain it for recovery.");
                    string destination=Path.Combine(collection!.ExportsFolder(),"raw-"+Path.GetFileName(directory)+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".zip");
                    await Task.Run(()=>ExportRecording(directory,destination));collectionExport=destination;
                    collectionMessage="Raw archive ready. Use Download export to save a copy.";
                }
                else if(action=="exportResults")
                {
                    collectionExport="";
                    collectionExport=await Task.Run(()=>collection!.ExportResults());
                    collectionMessage="Results workbook ready: every participant's results, shots and timeline.";
                }
                else throw new ArgumentException("Unknown data action.");
            }
            catch(Exception error){collectionError=error.Message;collectionMessage="Data action failed. Raw recordings are retained. Retry with Refresh.";}
            finally {collectionGate.Release();}
        }
        private string ImportCollectionFolders()
        {
            var errors=new System.Collections.Generic.List<string>();
            foreach(string directory in Directory.GetDirectories(collection!.Root))
            {
                if(!File.Exists(Path.Combine(directory,"events.ndjson")))continue;
                try {ImportCollectionRecording(directory);}
                catch(Exception error){errors.Add(Path.GetFileName(directory)+": "+error.Message);}
            }
            return String.Join("\n",errors);
        }
        private async Task SaveParticipantIdentityAsync()
        {
            if(!SeparateAdministrator || recording?.RunDirectory==null)return;
            // The recording carries only the pseudonymous participant code, durable even
            // if startup is interrupted. Every continuation gets its own copy so repeats
            // share the same profile. The optional name is kept only in the collection,
            // where removing the participant also removes it.
            string path=Path.Combine(recording.RunDirectory,"participant.json");
            string json=JsonConvert.SerializeObject(new {schemaVersion="elts.participant.v2",participantId=stationParticipant,synthetic=true},Formatting.Indented);
            await Task.Run(()=>PublishReviewFile(path,json));
            collection??=new ParticipantCollection(StationDataRoot);
            await collectionGate.WaitAsync();
            try {await Task.Run(()=>collection.SaveParticipant(stationParticipant,stationParticipantName));}
            catch(Exception error)
            {
                // The raw recording is unaffected; report the missing name separately.
                collectionError="Participant name was not saved: "+error.Message;
            }
            finally {collectionGate.Release();}
        }
        private void ImportCollectionRecording(string directory)
        {
            // Skip before reading raw JSON too, so excluded recovery files cannot
            // produce recurring import errors.
            if(collection!.IsRemoved(directory))return;
            string review=JsonConvert.SerializeObject(ReadRecordingReview(directory));
            // Recordings made without the browser administrator have no identity file
            // and use their folder name as the participant code.
            string code=Path.GetFileName(directory);
            string identityPath=Path.Combine(directory,"participant.json");
            if(File.Exists(identityPath))
            {
                if((File.GetAttributes(identityPath)&FileAttributes.ReparsePoint)!=0)throw new IOException("Participant identity cannot be a linked file.");
                code=(string?)JObject.Parse(File.ReadAllText(identityPath))["participantId"]??code;
            }
            collection!.Import(directory,code,review);
        }
        private async Task SyncCollectionRecording()
        {
            if(collection==null || recording?.RunDirectory==null)return;
            string directory=recording.RunDirectory;
            await collectionGate.WaitAsync();
            try
            {
                await Task.Run(()=>ImportCollectionRecording(directory));
                collectionProfiles=await Task.Run(()=>collection.Profiles());collectionReady=true;collectionError="";
                collectionMessage="Raw recording saved and results updated.";
            }
            catch(Exception error)
            {
                // A failed secondary index must never masquerade as a failed raw
                // writer or discard an otherwise recoverable recording.
                collectionError="Results need a refresh: "+error.Message;
                collectionMessage="Raw files saved. Open Data and choose Refresh to update the results.";
            }
            finally {collectionGate.Release();}
        }
    }
}
