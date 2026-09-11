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
                    collectionMessage="Saved records loaded · 50 per page.";
                }
                else if(action=="exportSessionRaw")
                {
                    collectionExport="";
                    string sessionId=(string?)command["id"]??"";
                    string directory=await Task.Run(()=>collection!.SessionDirectory(sessionId));
                    if(recording?.IsOpen==true && String.Equals(recording.RunDirectory,directory,StringComparison.OrdinalIgnoreCase))throw new IOException("Finish this recording before exporting its original files.");
                    if(!File.Exists(Path.Combine(directory,"session-summary.json")))throw new IOException("This recording is incomplete. Retain it for recovery.");
                    string destination=Path.Combine(collection!.Root,"exports","raw-"+Guid.ParseExact(sessionId,"N").ToString("N")+"-"+Guid.NewGuid().ToString("N")+".zip");
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    await Task.Run(()=>ExportRecording(directory,destination));collectionExport=destination;
                    collectionMessage="Original recording export ready. Use Download export to save a copy.";
                }
                else if(action=="exportDatabase" || action=="exportParticipant")
                {
                    collectionExport="";
                    string participant=action=="exportParticipant"?Guid.ParseExact((string?)command["id"]??"","N").ToString("N"):"";
                    collectionExport=await Task.Run(()=>collection!.Export(participant));
                    collectionMessage="Export ready. Use Download export to save a copy.";
                }
                else throw new ArgumentException("Unknown data action.");
            }
            catch(Exception error){collectionError=error.Message;collectionMessage="Data action failed. Raw recordings are retained. Retry with Refresh.";}
            finally {collectionGate.Release();}
        }
        private string ImportCollectionFolders()
        {
            // Old installations keep their original raw folders. Index both known
            // default locations without moving, renaming or altering any recording.
            var roots=new[]{collection!.Root,Path.Combine(Path.GetDirectoryName(collection.Root)!,"data","synthetic")}.Distinct();
            var errors=new System.Collections.Generic.List<string>();
            foreach(string rootPath in roots)
            {
                if(!Directory.Exists(rootPath))continue;
                if((File.GetAttributes(rootPath)&FileAttributes.ReparsePoint)!=0)continue;
                foreach(string directory in Directory.GetDirectories(rootPath))
                {
                    if(!File.Exists(Path.Combine(directory,"events.ndjson")))continue;
                    try {ImportCollectionRecording(directory);}
                    catch(Exception error){errors.Add(Path.GetFileName(directory)+": "+error.Message);}
                }
            }
            return String.Join("\n",errors);
        }
        private async Task SaveParticipantIdentityAsync()
        {
            if(!SeparateAdministrator || recording?.RunDirectory==null)return;
            // Identity is durable even if startup is interrupted before calibration.
            // Every continuation gets its own copy so repeats share the same profile.
            string path=Path.Combine(recording.RunDirectory,"participant.json");
            string json=JsonConvert.SerializeObject(new {schemaVersion="elts.participant.v1",participantId=stationParticipant,
                participantName=stationParticipantName,initialNotes=stationInitialNotes,synthetic=true},Formatting.Indented);
            await Task.Run(()=>PublishReviewFile(path,json));
        }
        private void ImportCollectionRecording(string directory)
        {
            string review=JsonConvert.SerializeObject(ReadRecordingReview(directory));
            var document=JObject.Parse(review);
            string code=Path.GetFileName(directory),name="";
            foreach(var note in document["notes"]??new JArray())
            {
                string text=(string?)note["text"]??"";
                if(!text.StartsWith("Participant details: ",StringComparison.Ordinal))continue;
                var details=JObject.Parse(text.Substring("Participant details: ".Length));
                code=(string?)details["participantId"]??code;name=(string?)details["participantName"]??"";break;
            }
            string identityPath=Path.Combine(directory,"participant.json");
            if(File.Exists(identityPath))
            {
                if((File.GetAttributes(identityPath)&FileAttributes.ReparsePoint)!=0)throw new IOException("Participant identity cannot be a linked file.");
                var identity=JObject.Parse(File.ReadAllText(identityPath));
                code=(string?)identity["participantId"]??code;name=(string?)identity["participantName"]??name;
            }
            collection!.Import(directory,code,name,review);
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
                collectionMessage="Raw recording and SQL copy saved.";
            }
            catch(Exception error)
            {
                // A failed secondary index must never masquerade as a failed raw
                // writer or discard an otherwise recoverable recording.
                collectionError="SQL copy needs retry: "+error.Message;
                collectionMessage="Raw files saved. Open Data and Refresh to retry the SQL copy.";
            }
            finally {collectionGate.Release();}
        }
    }
}
