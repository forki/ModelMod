﻿namespace MMLaunch

open System
open System.IO

open YamlDotNet.RepresentationModel
open YamlDotNet.Serialization

open ViewModelUtil

module ModUtil =

    type YamlRef = {
        Type: string
        MeshPath: string
        VertDeclPath: string
        ExpectedPrimCount: int
        ExpectedVertCount: int
    }

    type YamlMod = {
        Type: string
        Ref: string
        MeshType: string
        MeshPath: string
    }

    let getOutputPath modRoot modName = Path.GetFullPath(Path.Combine(modRoot, modName))

    type ModFilePath = string
    type Message = string

    let addToModIndex (modRoot:string) (modFile:string):Result<unit,Message> =
        try
            Err("unimplemented")
        with 
            | e -> Err(e.Message)

    let createMod (modRoot:string) (modName:string) (srcMMObjFile:string):Result<ModFilePath,Message> = 
        try
            let modName = modName.Trim()
            if modName = "" then 
                failwith "Please enter a mod name"

            if (not (File.Exists srcMMObjFile)) then 
                failwith "Please verify that the source file exists"
        
            let modOutDir = getOutputPath modRoot modName
            if (Directory.Exists modOutDir) then 
                failwithf "Mod directory already exists, try a different mod name? dir: %s" modOutDir

            // make sure filename conforms to expected format
            let (|SnapshotFilename|_|) pattern str = 
                ModelMod.REUtil.checkGroupMatch pattern 4 str 
                |> ModelMod.REUtil.extract 1 int32 
                |> (fun (intParts: int [] option) ->
                        match intParts with
                        | None -> None
                        | Some parts -> // omit snapshot number but return primcount, vertcount
                            Some (parts.[1],parts.[2]))

            let pCount,vCount = 
                match srcMMObjFile.ToLowerInvariant() with 
                | SnapshotFilename @"snap_(\S+)_(\d+)p_(\d+)v.*" parts -> parts
                | _ -> failwithf "Illegal snapshot filename; cannot build a ref from it: %s" srcMMObjFile

            let srcBasename = Path.GetFileNameWithoutExtension(srcMMObjFile)
            let snapSrcDir = Path.GetDirectoryName(srcMMObjFile)
            let refBasename = modName + "Ref"
            let modBasename = modName + "Mod"

            Directory.CreateDirectory modOutDir |> ignore

            // copy vb declaration
            let vbDeclFile =
                let declExt = ".dat"
                let declSuffix = "_VBDecl"
                let declSrc = Path.Combine(snapSrcDir, srcBasename + declSuffix + declExt)
                if not (File.Exists(declSrc)) then failwithf "No decl source file found; it is required: %s" declSrc
                else
                    let newDeclFile = Path.Combine(modOutDir, refBasename + declSuffix + declExt)
                    File.Copy(declSrc,newDeclFile)
                    newDeclFile

            // copy texture file
            let texFile = 
                let texExt = ".dds"
                let texSuffix = "_texture0"
                let texSrc = Path.Combine(snapSrcDir, srcBasename + texSuffix + texExt)
                if not (File.Exists(texSrc)) then None
                else
                    let newTexFile = Path.Combine(modOutDir, refBasename + texExt)
                    File.Copy(texSrc,newTexFile)
                    Some newTexFile

            // copy mtl file and rename texture
            let mtlFile = 
                let mtlExt = ".mtl"
                let mtlSrc = Path.Combine(snapSrcDir, srcBasename + mtlExt)
                if not (File.Exists(mtlSrc)) then None
                else
                    let newMtlFile = Path.Combine(modOutDir, refBasename + mtlExt)
                    let fDat = File.ReadAllLines(mtlSrc)
                    let fDat = fDat |> Array.map (fun line -> 
                        match line with
                        | l when texFile <> None && l.StartsWith("map_Kd ") -> "map_Kd " + Path.GetFileName(Option.get texFile)
                        | l -> l)                            
                    File.WriteAllLines(newMtlFile, fDat)
                    Some newMtlFile

            // copy mmobj and rename mtl
            let refMMObjFile = 
                // we already checked that the src exists
                let newMMObjFile = Path.Combine(modOutDir, refBasename + ".mmobj")
                let fDat = File.ReadAllLines(srcMMObjFile)
                let fDat = fDat |> Array.map (fun line ->
                    match line with
                    | l when mtlFile <> None && l.StartsWith("mtllib ") -> "mtllib " +  Path.GetFileName(Option.get mtlFile)
                    | l when l.StartsWith("o ") -> "o " + modName 
                    | l -> l)
                File.WriteAllLines(newMMObjFile, fDat)
                newMMObjFile

            // generate a default mod file that is a copy of the ref
            let modMMObjFile = 
                let modMMObjFile = Path.Combine(modOutDir, modBasename + ".mmobj")
                File.Copy(refMMObjFile,modMMObjFile)
                modMMObjFile

            // generate ref yaml
            let refYamlFile = 
                let refYamlFile = Path.Combine(modOutDir, refBasename + ".yaml")
                let refObj = {
                    YamlRef.Type = "Reference"
                    YamlRef.MeshPath = Path.GetFileName(refMMObjFile)
                    YamlRef.VertDeclPath = Path.GetFileName(vbDeclFile)
                    YamlRef.ExpectedPrimCount = pCount
                    YamlRef.ExpectedVertCount = vCount
                }
                let sr = new Serializer()
                use sw = new StreamWriter(refYamlFile)
                sr.Serialize(sw, refObj) 
                refYamlFile

            // generate mod yaml 
            let modYamlFile = 
                let modYamlFile = Path.Combine(modOutDir, modBasename + ".yaml")
                let modObj = { 
                    YamlMod.Type = "Mod"
                    MeshType = "GPUReplacement"
                    Ref = refBasename
                    MeshPath = Path.GetFileName(modMMObjFile)
                }
                let sr = new Serializer()
                use sw = new StreamWriter(modYamlFile)
                sr.Serialize(sw, modObj) 
                modYamlFile

            Ok(modMMObjFile)
        with 
            | e -> Err(e.Message)
