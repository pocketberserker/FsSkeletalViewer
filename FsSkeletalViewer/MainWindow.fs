namespace SkeletalViewer

open System.Diagnostics
open System.Linq
open System.Threading
open System.Reactive.Linq
open System.Windows
open System.Windows.Documents
open System.Windows.Controls
open System.Windows.Input
open System.Windows.Threading
open System.Windows.Media.Imaging
open Microsoft.Research.Kinect.Nui
open Microsoft.Samples.Kinect.WpfViewers

type MainWindow() as this =

  let minKinectCount = 1
  let maxKinectCount = 2

  let window =
    Application.LoadComponent(new System.Uri("/FsSkeletalViewer;component/MainWindow.xaml", System.UriKind.Relative)) :?> Window

  let kinectRequiredOrEnabled = window.FindName "kinectRequiredOrEnabled" :?> TextBlock
  let viewerHolder = window.FindName "viewerHolder" :?> ItemsControl
  let insertKinectSensor = window.FindName "insertKinectSensor" :?> StackPanel
  let insertAnotherKinectSensor = window.FindName "insertAnotherKinectSensor" :?> TextBlock
  let switchToAnotherKinectSensor = window.FindName "switchToAnotherKinectSensor" :?> TextBlock

  let mutable kinectViewerDictionary : (UserControl * KinectDiagnosticViewer) list = []

  let syncContext =
    if SynchronizationContext.Current = null then SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext())
    SynchronizationContext.Current

  let updateUIBasedOnKinectCount () =
    match Runtime.Kinects.Count with
    | 0 ->
      insertKinectSensor.Visibility <- System.Windows.Visibility.Visible
      insertAnotherKinectSensor.Visibility <- System.Windows.Visibility.Collapsed
      switchToAnotherKinectSensor.Visibility <- System.Windows.Visibility.Collapsed
    | _ ->
      insertKinectSensor.Visibility <- System.Windows.Visibility.Collapsed
      if viewerHolder.Items.Count < maxKinectCount then
        insertAnotherKinectSensor.Visibility <- System.Windows.Visibility.Visible
        switchToAnotherKinectSensor.Visibility <- System.Windows.Visibility.Collapsed
      else
        insertAnotherKinectSensor.Visibility <- System.Windows.Visibility.Collapsed
        switchToAnotherKinectSensor.Visibility <- if Runtime.Kinects.Count > maxKinectCount then System.Windows.Visibility.Visible else System.Windows.Visibility.Collapsed
    
    for i in 0 .. viewerHolder.Items.Count - 1 do
      let control = viewerHolder.Items.[i] :?> UserControl
      if control <> null then kinectViewerDictionary |> List.find (fun (c,viewer) -> c = control) |> (fun (_,viewer) -> viewer.UpdateUi() )

  let getViewer (sender:obj) =
    let rec getViewer' (sender:obj) =
      if sender <> null && sender :? FrameworkElement then
        let sender = box (sender :?> FrameworkElement).Parent
        if sender :? UserControl then kinectViewerDictionary |> List.find (fun (c,viewer) -> obj.ReferenceEquals(c,sender)) |> snd else getViewer' sender
      else Unchecked.defaultof<KinectDiagnosticViewer>

    getViewer' sender

  let createMouseLeftButtonDown (viewer:UserControl) =
    viewer.MouseLeftButtonDown
    |> Observable.subscribe begin
        fun e ->
          let currentSkeletalViewer = this.SkeletalDiagnosticViewer
          let thisViewer = getViewer e.Source
          if (box currentSkeletalViewer) <> null && currentSkeletalViewer <> thisViewer then
            let otherKinect = currentSkeletalViewer.Kinect
            let thisKinect = thisViewer.Kinect
            currentSkeletalViewer.Kinect <- None
            thisViewer.Kinect <- thisKinect
            currentSkeletalViewer.Kinect <- otherKinect
          elif this.SkeletalEngineAvailable then thisViewer.ReInitRuntime()
      end
    |> ignore

  let addKinectViewer runtime =
    let kinectViewer = new KinectDiagnosticViewer()
    kinectViewer.KinectDepthViewer.Control |> createMouseLeftButtonDown
    kinectViewer.Kinect <- Some runtime
    kinectViewer.Control |> viewerHolder.Items.Add |> ignore
    kinectViewerDictionary <- (kinectViewer.Control,kinectViewer) :: kinectViewerDictionary

  let findViewer (runtime:Runtime) = 
    viewerHolder.Items.OfType<UserControl>()
    |> Seq.collect (fun control -> kinectViewerDictionary |> List.find (fun (c,_) -> c = control) |> snd |> Seq.singleton)
    |> Seq.filter (fun v -> match v.Kinect with | Some k -> obj.ReferenceEquals(k, runtime) | None -> false)
    |> fun s -> s.FirstOrDefault()

  let removeKinectViewer runtime =
    let foundViewer = findViewer runtime
    foundViewer.Kinect <- None
    foundViewer.Control |> viewerHolder.Items.Remove
    kinectViewerDictionary <- kinectViewerDictionary |> List.filter (fun (c,v) -> c <> foundViewer.Control)

  let disableOrAddKinectViewer runtime =
    let foundViewer = findViewer runtime
    if box foundViewer <> null then runtime.Uninitialize()
    else addKinectViewer runtime

  let createAllKinectViewers () =
    let rec createKinectViewer = function
      | [] -> ()
      | runtime::runtimes ->
        if viewerHolder.Items.Count = maxKinectCount then ()
        else
          addKinectViewer runtime
          createKinectViewer runtimes
    
    Runtime.Kinects |> List.ofSeq |> createKinectViewer
    updateUIBasedOnKinectCount()

  let cleanUpAllKinectViewers () =
    for i in 0 .. viewerHolder.Items.Count - 1 do
      let control = viewerHolder.Items.[i] :?> UserControl
      if control <> null then kinectViewerDictionary |> List.find (fun (c,viewer) -> c = control) |> (fun (_,viewer) -> viewer.Kinect <- None)

    viewerHolder.Items.Clear()
    kinectViewerDictionary <- []

  let updateRuntimeOfKinectViewerToNextKinect (previousRuntime:Runtime) =
    
    let kinectViewer = kinectViewerDictionary |> List.find (fun (control,viewer) -> control = (viewerHolder.Items.[0] :?> UserControl)) |> snd
    
    let rec update (foundRuntime:bool) (runtimes:Runtime list) =
      match runtimes, foundRuntime with
      | [] , _ -> false
      | runtime::xs , true -> kinectViewer.Kinect <- Some runtime; true
      | runtime::xs , false ->
        match kinectViewer.Kinect with
        | Some kinect -> if runtime = kinect then xs |> update true else xs |> update foundRuntime
        | None -> xs |> update foundRuntime

    let foundRuntime = Runtime.Kinects |> List.ofSeq |> update false
    if foundRuntime = false && Runtime.Kinects.Count > 0 then kinectViewer.Kinect <- Some Runtime.Kinects.[0]
  
  let kinect_StatusChanged = Runtime.Kinects.StatusChanged.ObserveOn(syncContext)

  do kinect_StatusChanged
       .Subscribe begin
         fun (e:StatusChangedEventArgs) ->
           match e.Status with
           | KinectStatus.Connected -> 
             let viewer = findViewer e.KinectRuntime
             if box viewer <> null then viewer.Kinect <- Some e.KinectRuntime
             elif viewerHolder.Items.Count < maxKinectCount then addKinectViewer e.KinectRuntime
           | KinectStatus.Disconnected ->
             if Runtime.Kinects.Count >= maxKinectCount then updateRuntimeOfKinectViewerToNextKinect e.KinectRuntime
             else removeKinectViewer e.KinectRuntime
           | _ -> if e.Status.HasFlag(KinectStatus.Error) then disableOrAddKinectViewer e.KinectRuntime
           updateUIBasedOnKinectCount()
       end
     |> ignore

  do window.Loaded
     |> Observable.subscribe begin
         fun _ ->
           if minKinectCount > 0 then kinectRequiredOrEnabled.Text <- "Requires Kinect"
           else kinectRequiredOrEnabled.Text <- "Kinect Enabled"
           createAllKinectViewers()
           if SynchronizationContext.Current = null then SynchronizationContext.SetSynchronizationContext(new SynchronizationContext())
        end
     |> ignore

  do window.Closed
     |> Observable.subscribe (fun _ -> cleanUpAllKinectViewers() )
     |> ignore

  // switchSensors
  let switchLink = switchToAnotherKinectSensor.FindName "switchLink" :?> Hyperlink
  do switchLink.Click
     |> Observable.subscribe begin
         fun _ ->
           let control = viewerHolder.Items.[0] :?> UserControl
           let kinectViewer = kinectViewerDictionary |> List.find (fun (c,viewer) -> c = control) |> snd
           match kinectViewer.Kinect with
           | Some kinect -> updateRuntimeOfKinectViewerToNextKinect kinect
           | None -> ()
       end
     |> ignore

  // showMoreInfo
  let infoLink = window.FindName "infoLink" :?> Hyperlink
  do infoLink.Click
     |> Observable.subscribe begin
         fun e ->
           let hyperlink = e.OriginalSource :?> Hyperlink
           Process.Start(new ProcessStartInfo(hyperlink.NavigateUri.ToString())) |> ignore
           e.Handled <- true
       end
     |> ignore

  // mouseDown
  let grid = window.FindName "grid" :?> Grid
  do grid.MouseDown
     |> Observable.subscribe begin
         fun _ ->
           if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)) then
             cleanUpAllKinectViewers()
             createAllKinectViewers()
       end
     |> ignore

  member this.Window = window

  member internal this.SkeletalDiagnosticViewer
    with get() =
      viewerHolder.Items.OfType<UserControl>()
      |> Seq.collect (fun control -> kinectViewerDictionary |> List.find (fun (c,_) -> c = control) |> snd |> Seq.singleton)
      |> Seq.filter( fun v -> match v.Kinect with | Some kinect -> kinect.SkeletonEngine <> null | None -> false)
      |> fun s -> s.FirstOrDefault()
      
  member this.SkeletalEngineAvailable with get() = box this.SkeletalDiagnosticViewer = null