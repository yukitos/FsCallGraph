﻿namespace FsCallGraph

open System
open System.Collections.Generic
open System.ComponentModel
open System.IO
open System.Linq
open System.Reflection
open System.Windows.Input
open Graphviz4Net.Graphs
open Microsoft.Build.Evaluation
open Microsoft.FSharp.Compiler
open Microsoft.FSharp.Compiler.Ast
open Microsoft.FSharp.Compiler.SourceCodeServices

/// <summary>This class has no implementation because it's only for displaying an arrow on XAML.</summary>
type Arrow() = class end

/// <summary>This object contains an information about specific F# function that can be found in the specific project.</summary>
/// <remarks>This object will be used as key of Dictionary<object, int> inside graphviz4net, so we might need to implement IEquatable manually.</remarks>
type FunctionInfo =
    val propertyChanged : Event<PropertyChangedEventHandler, PropertyChangedEventArgs>
    val mutable name : string
    val mutable fullName : string
    val mutable compiledName : string
    val mutable location : Range.range

    new() =
        {
            propertyChanged = Event<_,_>()
            name = String.Empty
            fullName = String.Empty
            compiledName = String.Empty
            location = Range.range.Zero
        }
    
    override x.Equals(yobj) =
        match yobj with
        | :? FunctionInfo as y -> (x :> IEquatable<FunctionInfo>).Equals(y)
        | _ -> false

    override x.GetHashCode() =
        (x.fullName + x.compiledName + x.location.ToString()).GetHashCode()

    interface INotifyPropertyChanged with
        [<CLIEvent>]
        member x.PropertyChanged = x.propertyChanged.Publish

    interface IEquatable<FunctionInfo> with
        member x.Equals(y) =
            x.fullName = y.fullName
            && x.compiledName = y.compiledName
            && x.location = y.location

    /// <summary>Simplified function name.</summary>
    member x.Name
        with get() = x.name
        and set(v) =
            if not (EqualityComparer<string>.Default.Equals(v, x.name)) then
                x.name <- v
                x.propertyChanged.Trigger(x, new PropertyChangedEventArgs("Name"))

    /// <summary>Fully qualified function name.</summary>
    member x.FullName
        with get() = x.fullName
        and set(v) = x.fullName <- v

    /// <summary>Compiled function/value name.</summary>
    member x.CompiledName
        with get() = x.compiledName
        and set(v) = x.compiledName <- v

    /// <summary>Location where this functions is declared.</summary>
    member x.Location
        with get() = x.location
        and set(v) = x.location <- v

    member x.DisplayName
        with get() = x.Name

    member x.DisplayFullName
        with get() = x.FullName

    member x.DisplayFileName
        with get() = Path.GetFileName(x.location.FileName)

    member x.DisplaySymbolLocation
        with get() =
            "(" + x.location.StartLine.ToString() + "," + x.location.StartColumn.ToString() + ")"
            + "-(" + x.location.EndLine.ToString() + "," + x.location.EndColumn.ToString() + ")"

/// <summary>View-Model object bound to the main window.</summary>
type MainWindowViewModel =
    val graph : Graph<FunctionInfo>
    val propertyChanged : Event<PropertyChangedEventHandler, PropertyChangedEventArgs>
    val graphvizPath : Lazy<string>
    val openCommand : Lazy<ICommand>

    new() as x =
        {
            propertyChanged = Event<_,_>()
            graph = new Graph<FunctionInfo>()
            graphvizPath = lazy x.SearchGraphviz()
            openCommand = lazy x.OnOpenCommand()
        }
#if DEBUG
        // For debugging
        then

        // HACK: Load FsCallGraph.fsproj for test
        let projectFile = Path.Combine(x.GetAssemblyDirectory(), @"..\..\FsCallGraph.fsproj")
        x.Load(projectFile)
#endif

    interface INotifyPropertyChanged with
        [<CLIEvent>]
        member x.PropertyChanged = x.propertyChanged.Publish

    member x.Graph with get() = x.graph

    /// <summary>Graphviz binary path.</summary>
    /// <remarks>'dot.exe' must be found in the directory indicated by this.</remarks>
    member x.GraphvizPath
        with get() = x.graphvizPath.Value

    member x.OpenCommand
        with get() = x.openCommand.Value

    member x.GetAssemblyDirectory() =
        let asm = Assembly.GetExecutingAssembly()
        match asm with
        | null -> "."
        | _ -> Path.GetDirectoryName(asm.Location)

    member private x.SearchGraphviz() =
        let asmdir = x.GetAssemblyDirectory()
        let baseDirs =
            [
                Directory.GetCurrentDirectory()
                asmdir
                Path.Combine(asmdir, "..\\..") // for debug, search project directory.
            ]
        let directoryName = "graphviz"
        let dotExeFileName = "dot.exe"
        let path =
            baseDirs
            |> Seq.find(fun i -> File.Exists(Path.Combine(i, directoryName + "\\" + dotExeFileName)))
        Path.GetFullPath(Path.Combine(path, directoryName))

    member private x.OnOpenCommand() =
        let canExecuteChanged = Event<_,_>()
        { new ICommand with
            member y.CanExecute(param) = true
            [<CLIEvent>]
            member y.CanExecuteChanged = canExecuteChanged.Publish
            member y.Execute(param) =
                // TODO: implement here
                let projectFile = Path.Combine(x.GetAssemblyDirectory(), @"..\..\FsCallGraph.fsproj")
                x.Load(projectFile)
        }

    member private x.Load(projectFile) =
        let checker = InteractiveChecker.Create()
        
        let toolsVersion = "12.0"
        let globalProperties = new Dictionary<string, string>()
        globalProperties.Add("VisualStudioVersion", toolsVersion)

        let options = x.GetProjectOptionsFromProjectFile(projectFile, globalProperties, toolsVersion)
        let projectOptions =
            checker.GetProjectOptionsFromCommandLineArgs(projectFile, options)
        let wholeProjectResults =
            checker.ParseAndCheckProject(projectOptions)
            |> Async.RunSynchronously

        if wholeProjectResults.HasCriticalErrors then
            // TODO: implement here
            do()
        else

            let allMembersFunctionsAndValues (entities: IList<FSharpEntity>) =
                [ for e in entities do
                    for x in e.MembersFunctionsAndValues do
                        yield x]
            let fav = allMembersFunctionsAndValues wholeProjectResults.AssemblySignature.Entities
            let set = new HashSet<FSharpMemberFunctionOrValue>()
            
            // Add all symbols as vertex
            for f in fav do
                if not(set.Contains(f)) then
                    let info = new FunctionInfo(
                                Name = f.DisplayName,
                                FullName = f.FullName,
                                CompiledName = f.CompiledName,
                                Location = f.DeclarationLocation)
                    x.graph.AddVertex(info)
                    set.Add(f) |> ignore

            let findLocationInMembers members loc =
                members
                |> Seq.choose(fun m ->
                    match m with
                    | SynMemberDefn.Member(_, r)
                    | SynMemberDefn.LetBindings(_, _, _, r)
                    | SynMemberDefn.ImplicitCtor(_, _, _, _, r)
                    | SynMemberDefn.AutoProperty(_, _, _, _, _, _, _, _, _, _, r) ->
                        let ret = Range.rangeContainsRange r loc
                        if ret then Some(m)
                        else None
                    | _ -> None)

            let findLocationInTypes types loc =
                types
                |> Seq.choose(fun t ->
                    let (TypeDefn(_, repr, _, _)) = t
                    match repr with
                    | SynTypeDefnRepr.ObjectModel(_, members, _) ->
                        let r = findLocationInMembers members loc
                        if Seq.length r = 1 then Some(Seq.nth 0 r)
                        else None
                    | _ -> None)

            let findLocationInDeclarations decls loc =
                decls
                |> Seq.choose(fun d ->
                    match d with
                    | SynModuleDecl.Let(isRec, bindings, range) ->
                        for binding in bindings do
                            let (Binding(access, kind, inlin, mutabl, attrs, xmlDoc, data, pat, retInfo, body, m, sp)) = binding
                            // TODO: need to find symbol location from top level let bindings
                            do()
                        None
                    | SynModuleDecl.Types(types, range) ->
                        let r = findLocationInTypes types loc
                        if Seq.length r = 1 then Some(Seq.nth 0 r)
                        else None
                    | _ ->
                        None)
            
            let findLocationInModulesAndNamespaces modulesOrNss loc =
                let r = modulesOrNss
                        |> List.choose(fun moduleOrNs ->
                            let (SynModuleOrNamespace(_, _, decls, _, _, _, _)) = moduleOrNs
                            let r = findLocationInDeclarations decls loc
                            if Seq.length r = 1 then Some(Seq.nth 0 r)
                            else None)
                if List.length r = 1 then Some(List.nth r 0)
                else None

            let getRangeText (range:Range.range) =
                let lines = File.ReadAllLines(range.FileName)
                if range.StartLine = range.EndLine then
                    let line = lines.[range.StartLine-1]
                    line.Substring(range.StartColumn, range.EndColumn-range.StartColumn)
                else
                    let textLines =
                        [ for n in range.StartLine .. range.EndLine do
                            let line = lines.[n-1]
                            if n = range.StartLine then
                                yield line.Substring(range.StartColumn)
                            else if n = range.EndLine then
                                yield line.Substring(0, range.EndColumn)
                            else
                                yield line]
                        |> List.toArray
                    String.Join(Environment.NewLine, textLines)

            // Add all references
            for f in set do
                let destVertex = x.graph.Vertices.First(fun i -> i.Location = f.DeclarationLocation)
                let usesOfSymbol = wholeProjectResults.GetUsesOfSymbol(f)
                for s in usesOfSymbol.Where(fun i -> not i.IsFromDefinition) do
                    //System.Diagnostics.Debug.WriteLine(s.RangeAlternate.ToShortString() + "-->" + f.DeclarationLocation.ToShortString())
                    //System.Diagnostics.Debug.WriteLine((getRangeText s.RangeAlternate) + "-->" + (getRangeText f.DeclarationLocation))
                    
                    let loc = s.Symbol.DeclarationLocation.Value
                    System.Diagnostics.Debug.WriteLine("Dest: " + getRangeText loc)
                    
                    // Trying to detect who is invoking this symbol
                    
                    let fileLines = File.ReadAllLines(loc.FileName)
                    
                    let parseResults, checkFileResults =
                        checker.GetBackgroundCheckResultsForFileInProject(loc.FileName, projectOptions)
                        |> Async.RunSynchronously

                    let line = fileLines.[s.RangeAlternate.StartLine-1]
                    let symbol = checkFileResults.GetSymbolAtLocationAlternate(s.RangeAlternate.StartLine, s.RangeAlternate.EndColumn, line, [ getRangeText s.RangeAlternate ])
                    
                    let tree = parseResults.ParseTree
                    match tree.Value with
                    | ParsedInput.ImplFile(implFile) ->
                        let (ParsedImplFileInput(_, _, _, _, _, modules, _)) = implFile
                        let r = findLocationInModulesAndNamespaces modules s.RangeAlternate
                        if Option.isSome r then
                            match r.Value with
                            | SynMemberDefn.Member(binding, _) ->
                                let (Binding(access, kind, inlin, mutabl, attrs, xmlDoc, data, pat, retInfo, body, m, sp)) = binding
                                match pat with
                                | SynPat.LongIdent(ident, b, c, d, e, range) ->
                                    let line = fileLines.[range.StartLine-1] + Environment.NewLine
                                    let sym =
                                        if ident.Lid.Length > 1 then ident.Lid.[1]
                                        else ident.Lid.[0]
                                    let symbolName = sym.idText
                                    let symbol = checkFileResults.GetSymbolAtLocationAlternate(range.StartLine, range.EndColumn, line, [symbolName])
                                    if Option.isSome symbol then
                                        match symbol.Value with
                                        | :? FSharpMemberFunctionOrValue as y -> 
                                            if set.Contains(y) then
                                                System.Diagnostics.Debug.WriteLine("Source: " + getRangeText y.DeclarationLocation)
                                                let srcVertex = x.graph.Vertices.First(fun i -> i.Location = y.DeclarationLocation)
                                                x.graph.AddEdge(new Edge<FunctionInfo>(srcVertex, destVertex, new Arrow()))
                                        | _ -> do()
                            | _ -> 
                                do()
                    | _ -> failwith "*.fsi is not supported."

    member private x.GetProjectOptionsFromProjectFile(path, globalProperties, toolsVersion) =
        let baseDirectory = Path.GetDirectoryName(path)
        
        let project = new Project(path, globalProperties, toolsVersion)
        let properties = project.Properties
        let references = project.Items.Where(fun i -> i.ItemType = "Reference")
        let compiles = project.Items.Where(fun i -> i.ItemType = "Compile")
        
        let programFilesDir = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        
        let getProperty(props:ICollection<ProjectProperty>, name) =
            let p = props.FirstOrDefault(fun i -> i.Name = name)
            match p with
            | null -> null
            | x -> x.EvaluatedValue

        let getPropertyAs(props, name, defaultValue, converter) =
            let v = getProperty(props, name)
            match v with
            | null -> defaultValue
            | x -> converter(x)
        
        let getPropertyAsBool(props, name) =
            getPropertyAs(props, name, false, Convert.ToBoolean)

        let getExtensionFromOutputType(outputTypeInLowerCase:string) =
            match outputTypeInLowerCase with
            | "exe" | "winexe" -> ".exe"
            | "library" -> ".dll"
            | "module" -> ".module"
            | _ -> failwith "Unsupported OutputType: " + outputTypeInLowerCase

        [|
            yield "--simpleresolution"
            yield "--noframework"
            yield "--fullpaths"
            yield "--flaterrors"
            
            let debugType = getProperty(properties, "DebugType")
            yield "--debug:" + debugType
            
            if getPropertyAsBool(properties, "Optimize") then yield "--optimize+" else yield "--optimize-"
            
            let consts = getProperty(properties, "DefineConstants")
            if not (String.IsNullOrEmpty(consts)) then
                for c in consts.Split(';') do
                    yield "--define:" + c
            
            yield "--warn:" + getProperty(properties, "WarningLevel")
            
            let docfile = getProperty(properties, "DocumentationFile")
            if not (String.IsNullOrEmpty(docfile)) then
                yield "--doc:" + Path.Combine(baseDirectory, docfile)
            
            let outputPath = getProperty(properties, "OutputPath")
            let assemblyName = getProperty(properties, "AssemblyName")
            let outputType = getProperty(properties, "OutputType").ToLower()
            let ext = getExtensionFromOutputType(outputType)
            yield "--out:" + Path.Combine(baseDirectory, outputPath) + assemblyName + ext

            yield "--target:" + outputType

            let refAssemblyBaseDir = Path.Combine(programFilesDir, "Reference Assemblies\\Microsoft")
            let netFrameworkDir = Path.Combine(refAssemblyBaseDir, "Framework\\.NETFramework\\v4.0")
            let fsharpDir = Path.Combine(refAssemblyBaseDir, "FSharp\\.NETFramework\\v4.0\\4.3.1.0")
            for r in references do
                let name = r.EvaluatedInclude.Split(',').[0]
                let hintPath = r.GetMetadataValue("HintPath")
                if String.IsNullOrEmpty(hintPath) then
                    let dir = if name.ToLower() = "fsharp.core" then fsharpDir else netFrameworkDir
                    yield "-r:" + Path.Combine(dir, name + ".dll")
                else
                    yield "-r:" + Path.Combine(baseDirectory, hintPath)

            for f in compiles do
                yield Path.Combine(baseDirectory, f.EvaluatedInclude)
        |]
