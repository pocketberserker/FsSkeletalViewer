open System
open System.Windows
open SkeletalViewer

module Program =

  [<STAThread>]
  [<EntryPoint>]
  let main _ = (new MainWindow()).Window |> (new Application()).Run