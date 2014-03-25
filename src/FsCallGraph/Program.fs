open System
open System.Collections.ObjectModel
open System.IO
open System.Windows
open System.Windows.Controls
open System.Windows.Markup

[<STAThread>]
[<EntryPoint>]
let main argv =
    let uri = "MainWindow.xaml"
    let mainWindow = Application.LoadComponent(
                        new Uri(uri, UriKind.Relative)) :?> Window
    mainWindow.DataContext <- new FsCallGraph.MainWindowViewModel()
    (new Application()).Run(mainWindow)
