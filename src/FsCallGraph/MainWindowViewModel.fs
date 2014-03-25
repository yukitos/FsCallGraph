namespace FsCallGraph

open System
open System.Collections.Generic
open System.ComponentModel
open System.IO
open System.Linq
open System.Reflection
open System.Windows.Input
open Graphviz4Net.Graphs

/// <summary>This class has no implementation because it's only for displaying an arrow on XAML.</summary>
type Arrow() = class end

/// <summary>This object contains an information about specific F# function that can be found in the specific project.</summary>
/// <remarks>This object will be used as key of Dictionary<object, int> inside graphviz4net, so we might need to implement IEquatable manually.</remarks>
type FunctionInfo =
    val propertyChanged : Event<PropertyChangedEventHandler, PropertyChangedEventArgs>
    val mutable name : string
    val mutable fullname : string

    new() =
        {
            propertyChanged = Event<_,_>()
            name = String.Empty
            fullname = String.Empty
        }
    
    override x.Equals(yobj) =
        match yobj with
        | :? FunctionInfo as y -> x.fullname = y.fullname
        | _ -> false

    override x.GetHashCode() = x.fullname.GetHashCode()

    interface INotifyPropertyChanged with
        [<CLIEvent>]
        member x.PropertyChanged = x.propertyChanged.Publish

    interface IEquatable<FunctionInfo> with
        member x.Equals(yobj) = x.fullname = yobj.fullname

    /// <summary>Simplified function name.</summary>
    member x.Name
        with get() = x.name
        and set(v) =
            if not (EqualityComparer<string>.Default.Equals(v, x.name)) then
                x.name <- v
                x.propertyChanged.Trigger(x, new PropertyChangedEventArgs("Name"))

    /// <summary>Fully qualified function name.</summary>
    member x.Fullname
        with get() = x.fullname
        and set(v) = x.fullname <- v

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

        // Add 10 function names and make relations between them randomly
        
        let functions = seq { for i in 1 .. 10 do yield new FunctionInfo(Name = "func" + i.ToString(), Fullname = "func" + i.ToString()) }
        functions |> Seq.iter x.graph.AddVertex

        let (--->) f1 f2 =
            let i1 = Seq.nth f1 functions
            let i2 = Seq.nth f2 functions
            x.graph.AddEdge(new Edge<FunctionInfo>(i1, i2, new Arrow()))
        
        let r = new Random()
        for i in 0 .. 10 do
            let n1 = r.Next(10)
            let n2 = r.Next(10)
            if x.graph.Edges.Count(fun i -> i.Source = box (Seq.nth n1 functions) && i.Destination =box (Seq.nth n2 functions)) = 0 then
                n1 ---> n2
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

    member private x.SearchGraphviz() =
        let asm = Assembly.GetExecutingAssembly()
        let asmdir =
            match asm with
            | null -> "."
            | _ -> Path.GetDirectoryName(asm.Location)
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
            member y.Execute(param) = x.Load()
            [<CLIEvent>]
            member y.CanExecuteChanged = canExecuteChanged.Publish }

    member private x.Load() =
        // TODO: implement here
        do()
