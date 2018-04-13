namespace Aardium

open System
open System.IO
open System.IO.Compression
open System.Net
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open System.Diagnostics
open Aardvark.Base

[<Struct>]
type Progress(received : Mem, total : Mem) =
    member x.Received = received
    member x.Total = total
    member x.Relative = float received.Bytes / float total.Bytes

    override x.ToString() =
        sprintf "%.2f%%" (100.0 * float received.Bytes / float total.Bytes)

module Tools =
    
    type private Message =
        | Log of string
        | Error of string

    let unzip (file : string) (folder : string) =
        try
            if Directory.Exists folder then Directory.Delete(folder, true)
            ZipFile.ExtractToDirectory(file, folder)
        with _ ->
            if Directory.Exists folder then Directory.Delete(folder, true)
            reraise()

    let download (progress : Progress -> unit) (url : string) (file : string) =
        try
            use c = new HttpClient()

            let response = c.GetAsync(System.Uri url, HttpCompletionOption.ResponseHeadersRead).Result
            let len = 
                let f = response.Content.Headers.ContentLength
                if f.HasValue then f.Value
                else 1L <<< 30
                
            let mutable lastProgress = Progress(Mem.Zero, Mem len)
            progress lastProgress
            let sw = System.Diagnostics.Stopwatch.StartNew()


            use stream = response.Content.ReadAsStreamAsync().Result
            if File.Exists file then File.Delete file
            use output = File.OpenWrite(file)


            let buffer : byte[] = Array.zeroCreate (4 <<< 20)
            
            let mutable remaining = len
            let mutable read = 0L
            while remaining > 0L do
                let cnt = int (min remaining buffer.LongLength)
                let r = stream.Read(buffer, 0, cnt)
                output.Write(buffer, 0, r)
            
                remaining <- remaining - int64 r
                read <- read + int64 r


                let p = Progress(Mem read, Mem len)
                if sw.Elapsed.TotalSeconds >= 0.1 || p.Relative - lastProgress.Relative > 0.05 then
                    progress p
                    lastProgress <- p
                    sw.Restart()
                    
        with _ ->
            if File.Exists file then File.Delete file
            reraise()
          
    let startThread (f : unit -> unit) =
        let t = new Thread(ThreadStart(f), IsBackground = true)
        t.Start()

    let exec (file : string) (args : string[]) =
        let info = 
            ProcessStartInfo(
                file, 
                Arguments = String.concat " " args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            )
            
        info.EnvironmentVariables.["ELECTRON_ENABLE_LOGGING"] <- "1"
        info.Environment.["ELECTRON_ENABLE_LOGGING"] <- "1"

        let proc = Process.Start(info)
        use log = new System.Collections.Concurrent.BlockingCollection<Message>()

        proc.OutputDataReceived.Add (fun e ->
            if not (String.IsNullOrWhiteSpace e.Data) then
                log.Add (Log e.Data)
        )
        
        proc.ErrorDataReceived.Add (fun e ->
            if not (String.IsNullOrWhiteSpace e.Data) then
                log.Add (Error e.Data)
        )
        
        proc.BeginOutputReadLine()
        proc.BeginErrorReadLine()
        
        let cancel = new CancellationTokenSource()

        startThread (fun () ->
            proc.WaitForExit()
            cancel.Cancel()
        )
        
        try
            while true do
                let msg = log.Take(cancel.Token)
                match msg with
                    | Log msg -> Report.Line("{0}", msg)
                    | Error msg -> Report.Warn("{0}", msg)
        with :? OperationCanceledException ->
            ()


module Aardium =

    let feed = "https://vrvis.myget.org/F/aardvark_public/api/v2/package"
    let packageBaseName = "Aardium"
    let version = "1.0.0"

    let private platform =
        match Environment.OSVersion with
            | Windows -> "Win32"
            | Linux -> "Linux"
            | Mac -> "Darwin"

    let private arch =
        match sizeof<nativeint> with
            | 4 -> "x86"
            | 8 -> "x64"
            | v -> failwithf "bad bitness: %A" v

    let private packageName = sprintf "%s-%s-%s" packageBaseName platform arch

    let private exeName =
        match Environment.OSVersion with
            | Windows -> "Aardium.exe"
            | Linux -> "Aardium"
            | Mac -> "Aardium.app"

    let private cachePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aardium")

    let init() =
        let info = DirectoryInfo cachePath
        if not info.Exists then info.Create()

        let aardiumPath = Path.Combine(cachePath, arch, version)
        let info = DirectoryInfo aardiumPath

        if not info.Exists then
            info.Create()

            let fileName = sprintf "%s.%s.nupkg" packageName version
            let tempFile = Path.Combine(cachePath, arch, fileName)
            let url = sprintf "%s/%s/%s" feed packageName version

            Log.startTimed "downloading %A" url
            Tools.download (fun p -> Report.Progress(p.Relative))  url tempFile
            Log.stop()

            Log.startTimed "unpacking"
            Tools.unzip tempFile aardiumPath
            Log.stop()

    let runUrl (url : string) =
        let aardiumPath = Path.Combine(cachePath, arch, version, "tools", exeName)
        if File.Exists aardiumPath then
            Tools.exec aardiumPath [|"dummy"; "\"" + url + "\"" |]
        else
            failwithf "could not locate aardium"

    let run() =
        runUrl "http://localhost:4321/"
        
