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
        private ParticipantDataset? datasetLease;
        private bool removingDataset;
        private string removalRequest="", removalState="", removalMessage="";
        public long DatasetVersion { get; private set; }
        public object DatasetRemoval => new {request=removalRequest, state=removalState, message=removalMessage, version=DatasetVersion};
        private ParticipantCollection? collection;
        private Task? collectionOperation;
        private object collectionProfiles=Array.Empty<object>(), collectionRows=Array.Empty<object>();
        private object? collectionProfile;
        private string collectionMessage="Preparing local storage…", collectionError="", collectionExport="";
        private bool collectionReady;
        private string collectionRowsSession="",collectionRowsStream="";
        private string collectionSelectedProfile="";
        private string collectionPendingDirectory="";
        private int collectionRowsPage;
        private long collectionRevision;
        public long CollectionRevision => collectionRevision;
        public object CollectionStatus => new { revision=collectionRevision, ready=collectionReady,
            busy=collectionOperation!=null && !collectionOperation.IsCompleted, message=collectionMessage,
            error=collectionError, path=collection?.Root??"", activeDirectory=recording?.IsOpen==true?recording.RunDirectory:"",
            closedPendingDirectory=collectionPendingDirectory };
        public string CollectionExportPath=>collectionExport;
        public object CollectionState=>new {profiles=collectionProfiles,profile=collectionProfile,rows=collectionRows,
            rowsSession=collectionRowsSession,rowsStream=collectionRowsStream,rowsPage=collectionRowsPage};

        public void InitializeCollection()
        {
            datasetLease??=new ParticipantDataset(StationDataRoot);
            collection=new ParticipantCollection(StationDataRoot);
            CollectionAction(new JObject { ["action"]="refreshData" });
        }
        public void CollectionAction(JObject command)
        {
            if(collectionOperation!=null && !collectionOperation.IsCompleted)throw new InvalidOperationException("Data is still loading. Please wait.");
            collection??=new ParticipantCollection(StationDataRoot);
            if ((string?)command["action"] == "wipeDataset")
            {
                if (!StationCanStartParticipant || recording?.IsOpen == true)
                    throw new InvalidOperationException("Finish or abort the current session before removing participant data.");
                if ((string?)command["confirmation"] != "CONFIRM" || !Guid.TryParse((string?)command["request"], out _))
                    throw new ArgumentException("Type CONFIRM exactly to remove participant data.");
                if ((long?)command["version"] != DatasetVersion)
                    throw new InvalidOperationException("The dataset changed. Reopen the removal dialog and review it again.");
                removingDataset = true;
                removalRequest = (string)command["request"]!;
                removalState = "working";
                removalMessage = "Removing participant data…";
            }
            collectionOperation=RunCollectionAction(command);
        }
        private async Task RunCollectionAction(JObject command)
        {
            collectionMessage="Working…";collectionError="";
            string action=(string?)command["action"]??"";
            await collectionGate.WaitAsync();
            try
            {
                if (action == "wipeDataset")
                {
                    await RemoveParticipantDataset((string)command["confirmation"]!);
                    return;
                }
                await Task.Run(()=>collection!.Initialize());collectionReady=true;
                if(action=="refreshData")
                {
                    bool rebuild = (bool?)command["rebuild"] == true;
                    var result = await Task.Run(()=>RecordingIndex.Refresh(collection!,rebuild));
                    collectionProfiles=await Task.Run(()=>collection!.Profiles());
                    string selected=(string?)command["id"]??"";
                    collectionSelectedProfile=selected;
                    collectionProfile=selected.Length>0?await Task.Run(()=>collection!.Profile(selected)):null;
                    collectionError=result.Errors;
                    if(result.Errors.Length==0)collectionPendingDirectory="";
                    collectionMessage=$"Refreshed {result.Indexed} recording(s). {result.Unchanged} unchanged.";
                }
                else if(action=="viewParticipant")
                {
                    collectionSelectedProfile=(string?)command["id"]??"";
                    collectionProfile=await Task.Run(()=>collection!.Profile(collectionSelectedProfile));
                    collectionRows=Array.Empty<object>();collectionRowsSession="";collectionMessage="Participant profile loaded.";
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
                    collectionSelectedProfile=action=="removeRun"?selected:"";
                    collectionProfile=action=="removeRun" && selected.Length>0?await Task.Run(()=>collection!.Profile(selected)):null;
                    collectionRows=Array.Empty<object>();collectionRowsSession="";collectionRowsStream="";collectionRowsPage=0;
                    collectionExport="";
                    collectionMessage="Data removed from the viewer. Original recording files and existing exports are retained.";
                }
                else if(action=="exportSessionRaw")
                {
                    collectionExport="";
                    string sessionId=(string?)command["id"]??"";
                    string directory=await Task.Run(()=>collection!.SessionDirectory(sessionId));
                    if(recording?.IsOpen==true && String.Equals(recording.RunDirectory,directory,StringComparison.OrdinalIgnoreCase))throw new IOException("Finish this recording before exporting its original files.");
                    bool recovery=!File.Exists(Path.Combine(directory,"session-summary.json"));
                    if(recovery && (bool?)command["recovery"]!=true)throw new IOException("Choose Recovery copy for an interrupted recording.");
                    string prefix=recovery?"recovery-":"raw-";
                    string destination=Path.Combine(collection!.ExportsFolder(),prefix+Path.GetFileName(directory)+"-"+Guid.NewGuid().ToString("N").Substring(0,6)+".zip");
                    await Task.Run(()=>ExportRecording(directory,destination));collectionExport=destination;
                    collectionMessage=recovery?"Recovery copy ready. This recording has no closure summary.":"Raw archive ready. Use Download export to save a copy.";
                }
                else if(action=="verifyRecording")
                {
                    string id=(string?)command["id"]??"";
                    try { collectionMessage=await Task.Run(()=>RecordingIndex.VerifyRecording(collection!,id)); }
                    finally
                    {
                        string selected=(string?)command["participant"]??"";
                        if(selected.Length>0)collectionProfile=await Task.Run(()=>collection!.Profile(selected));
                    }
                }
                else if(action=="exportResults" || action=="previewResults")
                {
                    string scope=(string?)command["scope"]??"all", participant=(string?)command["participant"]??"";
                    string visit=(string?)command["visit"]??"", from=(string?)command["from"]??"", through=(string?)command["through"]??"";
                    if(action=="previewResults")
                        collectionMessage=await Task.Run(()=>collection!.PreviewExport(scope,participant,visit,from,through));
                    else
                    {
                        collectionExport="";
                        collectionExport=await Task.Run(()=>collection!.ExportResults(scope,participant,visit,from,through));
                        collectionMessage="Results workbook ready. Included recordings are listed on the Recordings sheet.";
                    }
                }
                else throw new ArgumentException("Unknown data action.");
            }
            catch(Exception error)
            {
                collectionError=error.Message;
                if (removingDataset)
                {
                    removalState="error";
                    removalMessage="Removal did not finish. "+error.Message+" Check the data folder and retry.";
                    collectionMessage=removalMessage;
                }
                else collectionMessage="Data action failed. Retry with Refresh.";
            }
            finally
            {
                // Export progress and errors use the lightweight live status.
                // Preparing a download leaves the historical payload unchanged.
                if(action!="previewResults" && action!="exportResults" && action!="exportSessionRaw")collectionRevision++;
                removingDataset=false;collectionGate.Release();
            }
        }
        private async Task RemoveParticipantDataset(string confirmation)
        {
            string profiles = CalibrationProfileDirectory;
            datasetLease ??= new ParticipantDataset(StationDataRoot);
            // A partial I/O failure must never leave a cached review or a download
            // referring to files that may already have been deleted.
            collectionReady = false;
            collectionPendingDirectory="";
            collectionSelectedProfile = "";
            collectionExport = ""; collectionProfile = null;
            collectionProfiles = collectionRows = Array.Empty<object>();
            collectionRowsSession = collectionRowsStream = ""; collectionRowsPage = 0;
            await Task.Run(() => datasetLease.Remove(profiles, confirmation));
            DatasetVersion++;
            // Clear the closed visit too. Otherwise shutdown would try to index it
            // again, and the old visit would still be eligible for a repeat.
            recording?.Dispose(); recording = null; engine = null; scenario = null;
            stationParticipant = stationVisitId = runId = "";
            stationStartedAt = 0; assignedOrder = null; orderDeviation = ""; stationCalibration = null; stationScreenDiffers = false; stationCalibrationProblem = "";
            stationCheckpoints.Clear(); stationCheckpointId = "";
            stationAutomaticallyArmedBlock = null; stationSaveStatus = "No recording yet.";
            setupPending = closing = TestArmed = false;
            visibleTargets.Clear(); view.DetachSession(); view.ClearParticipantCalibration();
            ResetTutorial(); ResetCalibration();
            lastMessage = "";
            await Task.Run(() => collection!.Initialize());
            collectionReady = true;
            removalState = "complete";
            collectionMessage = removalMessage = "Participant data removed. The station is ready for a fresh dataset.";
        }
        private async Task SaveParticipantIdentityAsync()
        {
            if(!SeparateAdministrator || recording?.RunDirectory==null)return;
            // The recording carries only the pseudonymous participant code, durable even
            // if startup is interrupted. Every continuation gets its own copy with the same
            // visit ID, so repeats share one profile and one visit.
            string path=Path.Combine(recording.RunDirectory,"participant.json");
            // Retain only the settings actually applied. A later replacement of the
            // profile must not change the interpretation of an existing recording.
            // Explicit values keep unrelated fields from legacy profiles out of raw data.
            var rig=view.Configuration.Rig;
            double[] Coordinates(Elts.Geometry.Vector3d point)=>new[]{point.X,point.Y,point.Z};
            var effectiveSettings = new {
                stagedConfigSha256=view.Configuration.EffectiveSha256, participantProfileSha256=stationCalibration?.Sha256,
                participantProfile=stationCalibration?.FileName,
                savedScreenMatchesBuild=stationCalibration==null?(bool?)null:!stationScreenDiffers,
                coordinateFrame="Unity room, metres; participant offsets in tracker-local axes",
                rigId=rig.RigId, screenOriginM=Coordinates(rig.Display.Origin),
                screenU=Coordinates(rig.Display.U),screenV=Coordinates(rig.Display.V),
                screenWidthM=rig.Display.Width,screenHeightM=rig.Display.Height,
                screenBottomHeightM=rig.ScreenBottomHeightAboveFloorM,
                headEyeOffsetM=Coordinates(rig.HeadEyeOffsetM),weaponMuzzleOffsetM=Coordinates(rig.WeaponMuzzleOffsetM),
                weaponBoreLocalDirection=Coordinates(rig.WeaponBoreLocalDirection)
            };
            string json=JsonConvert.SerializeObject(new {schemaVersion="elts.participant.v4",participantId=stationParticipant,visitId=stationVisitId,synthetic=true,effectiveSettings,
                configuredOrder=StationOrder,assignedOrder,orderDeviation},Formatting.Indented);
            await Task.Run(()=>PublishReviewFile(path,json));
            collection??=new ParticipantCollection(StationDataRoot);
            await collectionGate.WaitAsync();
            try
            {
                await Task.Run(()=>collection.SaveParticipant(stationParticipant));
                collectionProfiles=await Task.Run(()=>collection.Profiles());
                collectionRevision++;
            }
            catch(Exception error)
            {
                // The raw recording is unaffected; report the missing collection entry separately.
                collectionError="Participant ID could not be indexed: "+error.Message;
            }
            finally {collectionGate.Release();}
        }
        private async Task SyncCollectionRecording(bool closed=false)
        {
            if(collection==null || recording?.RunDirectory==null)return;
            string directory=recording.RunDirectory;
            if(closed)collectionPendingDirectory=directory;
            await collectionGate.WaitAsync();
            try
            {
                await Task.Run(()=>RecordingIndex.IndexRecording(collection,directory));
                if(collectionPendingDirectory==directory)collectionPendingDirectory="";
                collectionProfiles=await Task.Run(()=>collection.Profiles());collectionReady=true;collectionError="";
                // A checkpoint must update the review already open in the browser.
                // All reads use the same gate as selecting a different participant.
                if(collectionSelectedProfile.Length>0)
                    collectionProfile=await Task.Run(()=>collection.Profile(collectionSelectedProfile));
                if(collectionRowsSession.Length>0)
                    collectionRows=await Task.Run(()=>collection.Records(collectionRowsSession,collectionRowsStream,collectionRowsPage));
                collectionMessage="Raw recording saved and results updated.";
            }
            catch(Exception error)
            {
                // A failed secondary index must never masquerade as a failed raw
                // writer or discard an otherwise recoverable recording.
                collectionError="Results need a refresh: "+error.Message;
                collectionMessage="Raw files saved. Open Data and choose Refresh to update the results.";
            }
            finally {collectionRevision++;collectionGate.Release();}
        }
    }
}
