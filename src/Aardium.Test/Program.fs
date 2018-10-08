﻿// Learn more about F# at http://fsharp.org

open System
open Aardvark.Base
open Aardium

open System
open System.IO
open System.IO.Pipes
open System.IO.MemoryMappedFiles
open Microsoft.Win32.SafeHandles



[<EntryPoint>]
let main argv =
    Aardium.init()

    Aardium.run { 
        url "http://ask.aardvark.graphics"
    }

    0 
