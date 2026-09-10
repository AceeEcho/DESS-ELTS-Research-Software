#nullable enable
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Newtonsoft.Json.Linq;

namespace Elts.Operator
{
    /// <summary>
    /// Small loopback-only HTTP bridge for the administrator window. Unity work
    /// stays on the main thread. The socket worker serves immutable snapshots and
    /// admits authenticated commands to a bounded queue; it never calls Unity APIs.
    /// </summary>
    public sealed class AdministratorServer : IDisposable
    {
        public const int PreferredPort=18764, MaximumCommandBytes=16384;
        public sealed class PendingCommand
        {
            public readonly JObject Payload;
            private readonly long enqueuedAt=Stopwatch.GetTimestamp();
            private int status;
            public readonly TaskCompletionSource<string> Completion=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            public PendingCommand(JObject payload){Payload=payload;}
            public bool TryBegin()
            {
                if((Stopwatch.GetTimestamp()-enqueuedAt)/(double)Stopwatch.Frequency>1.2){CancelIfQueued();return false;}
                return Interlocked.CompareExchange(ref status,1,0)==0;
            }
            public void CancelIfQueued()
            {
                if(Interlocked.CompareExchange(ref status,3,0)==0)
                    Completion.TrySetResult("{\"ok\":false,\"message\":\"Action expired before Unity received it. Please try again.\"}");
            }
            public void Complete(string result){Interlocked.Exchange(ref status,2);Completion.TrySetResult(result);}
        }
        private readonly TcpListener listener;
        private readonly Thread worker;
        private readonly string directory;
        private readonly ConcurrentQueue<PendingCommand> commands=new ConcurrentQueue<PendingCommand>();
        private volatile bool stopped;
        private byte[] state=Encoding.UTF8.GetBytes("{\"busy\":true,\"state\":\"Starting\"}");
        private byte[] picture=Array.Empty<byte>();
        public string Token {get;}=Guid.NewGuid().ToString("N");
        public string Origin {get;}
        public string Url => Origin+"/#"+Token;

        public AdministratorServer(string directory)
        {
            this.directory=directory;
            listener=new TcpListener(IPAddress.Loopback,PreferredPort);
            try{listener.Start(8);}
            catch(SocketException){listener=new TcpListener(IPAddress.Loopback,0);listener.Start(8);}
            Origin="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port;
            worker=new Thread(Serve){IsBackground=true,Name="ELTS administrator loopback"};worker.Start();
        }
        public void PublishState(string json)=>Volatile.Write(ref state,Encoding.UTF8.GetBytes(json));
        public void PublishImage(byte[] jpeg)=>Volatile.Write(ref picture,jpeg);
        public bool TryTakeCommand(out PendingCommand command)=>commands.TryDequeue(out command);
        private void Serve()
        {
            while(!stopped)
            {
                try
                {
                    using(var client=listener.AcceptTcpClient())
                    {
                        client.ReceiveTimeout=1500;client.SendTimeout=1500;
                        using(var stream=client.GetStream())Handle(stream);
                    }
                }
                catch(SocketException){if(stopped)return;}
                catch(IOException){ }
                catch(ObjectDisposedException){if(stopped)return;}
                catch(Exception){ /* A malformed request cannot terminate the bridge. */ }
            }
        }
        private void Handle(NetworkStream stream)
        {
            // Bound both headers and body. No chunked requests or file-system
            // routing are accepted; the browser needs only three static assets.
            var header=new MemoryStream();int matched=0;
            while(header.Length<8192 && matched<4)
            {
                int next=stream.ReadByte();if(next<0)return;header.WriteByte((byte)next);
                byte expected=matched%2==0?(byte)'\r':(byte)'\n';
                matched=next==expected?matched+1:(next=='\r'?1:0);
            }
            if(matched<4){Respond(stream,400,"text/plain",Encoding.UTF8.GetBytes("Header too large"));return;}
            string[] lines=Encoding.ASCII.GetString(header.ToArray()).Split(new[]{"\r\n"},StringSplitOptions.None);
            string[] request=lines[0].Split(' ');
            if(request.Length!=3 || !request[1].StartsWith("/",StringComparison.Ordinal)){Respond(stream,400,"text/plain",Array.Empty<byte>());return;}
            string method=request[0],route=request[1].Split('?')[0],token="",origin="",host="";int length=0;
            foreach(string line in lines)
            {
                int colon=line.IndexOf(':');if(colon<0)continue;
                string name=line.Substring(0,colon).Trim().ToLowerInvariant(),value=line.Substring(colon+1).Trim();
                if(name=="x-elts-token")token=value;
                if(name=="origin")origin=value;
                if(name=="host")host=value;
                if(name=="content-length" && (!Int32.TryParse(value,out length)||length<0))length=MaximumCommandBytes+1;
            }
            if(host!=new Uri(Origin).Authority){Respond(stream,403,"text/plain",Array.Empty<byte>());return;}
            if(route=="/api/view.jpg" && request[1].Contains("token="))
            {
                foreach(string part in request[1].Substring(request[1].IndexOf('?')+1).Split('&'))
                    if(part.StartsWith("token=",StringComparison.Ordinal))token=part.Substring(6);
            }
            if(route.StartsWith("/api/",StringComparison.Ordinal) && token!=Token)
            {Respond(stream,403,"application/json",Encoding.UTF8.GetBytes("{\"ok\":false,\"message\":\"Open the administrator link from this Unity session.\"}"));return;}
            if(method=="GET")
            {
                if(route=="/api/state"){Respond(stream,200,"application/json",Volatile.Read(ref state));return;}
                if(route=="/api/view.jpg")
                {var image=Volatile.Read(ref picture);Respond(stream,image.Length==0?204:200,"image/jpeg",image);return;}
                string? file=route=="/"?"index.html":route=="/styles.css"?"styles.css":route=="/app.js"?"app.js":null;
                if(file!=null && File.Exists(Path.Combine(directory,file)))
                {Respond(stream,200,file.EndsWith("css")?"text/css":file.EndsWith("js")?"text/javascript":"text/html",File.ReadAllBytes(Path.Combine(directory,file)));return;}
                Respond(stream,404,"text/plain",Encoding.UTF8.GetBytes("Not found"));return;
            }
            if(method!="POST" || route!="/api/command" || origin!=Origin || length>MaximumCommandBytes)
            {Respond(stream,400,"text/plain",Encoding.UTF8.GetBytes("Invalid command"));return;}
            var body=new byte[length];int read=0;
            while(read<length){int count=stream.Read(body,read,length-read);if(count==0)return;read+=count;}
            if(commands.Count>=8){Respond(stream,429,"application/json",Encoding.UTF8.GetBytes("{\"ok\":false,\"message\":\"Please wait for the current action.\"}"));return;}
            PendingCommand command;
            try{command=new PendingCommand(JObject.Parse(Encoding.UTF8.GetString(body)));}
            catch{Respond(stream,400,"application/json",Encoding.UTF8.GetBytes("{\"ok\":false,\"message\":\"Invalid JSON.\"}"));return;}
            commands.Enqueue(command);
            // A queued action is processed at the next Unity Update. Never wait
            // for an entire recording or preparation operation on the socket.
            if(!command.Completion.Task.Wait(1200))command.CancelIfQueued();
            string result=command.Completion.Task.IsCompleted?command.Completion.Task.Result:
                "{\"ok\":true,\"message\":\"Action is being processed.\"}";
            Respond(stream,200,"application/json",Encoding.UTF8.GetBytes(result));
        }
        private static void Respond(NetworkStream stream,int code,string type,byte[] bytes)
        {
            string status=code==200?"OK":code==204?"No Content":code==403?"Forbidden":code==404?"Not Found":"Bad Request";
            byte[] header=Encoding.ASCII.GetBytes("HTTP/1.1 "+code+" "+status+"\r\nContent-Type: "+type+"\r\nContent-Length: "+bytes.Length+
                "\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\n\r\n");
            stream.Write(header,0,header.Length);if(bytes.Length>0)stream.Write(bytes,0,bytes.Length);
        }
        public void Dispose()
        {
            stopped=true;listener.Stop();
            while(commands.TryDequeue(out var pending))pending.CancelIfQueued();
            worker.Join(1800);
        }
    }
}
