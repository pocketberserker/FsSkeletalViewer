namespace Microsoft.Samples.Kinect.WpfViewers

open System
open System.Windows
open System.Windows.Controls
open System.Windows.Media
open System.Windows.Shapes
open Microsoft.Research.Kinect.Nui

type KinectDiagnosticViewer() as this =

  let mutable kinect : Runtime option = None

  let mutable runtimeOptions : RuntimeOptions = RuntimeOptions.UseColor

  let kinectColorViewer = KinectColorViewer()
  let kinectDepthViewer = KinectDepthViewer()

  do
    kinectColorViewer.Control.Width <- 400.0
    kinectColorViewer.Control.Height <- 300.0
    kinectColorViewer.Control.Margin <- new Thickness(10.0, 0.0, 10.0, 10.0)
    kinectDepthViewer.Control.Width <- 400.0
    kinectDepthViewer.Control.Height <- 300.0
    kinectDepthViewer.Control.Margin <- new Thickness(10.0, 0.0, 10.0, 10.0)

  let control =
    Application.LoadComponent(new System.Uri("/Microsoft.Samples.Kinect.WpfViewers;component/KinectDiagnosticViewer.xaml", System.UriKind.Relative)) :?> UserControl

  do
    control.DataContext <- kinectDepthViewer
    let colorStackPanel = control.FindName "colorStackPanel" :?> StackPanel
    kinectColorViewer.Control |> colorStackPanel.Children.Add |> ignore
    let depthStackPanel = control.FindName "depthStackPanel" :?> StackPanel
    kinectDepthViewer.Control |> depthStackPanel.Children.Add |> ignore

  let kinectIndex = control.FindName "kinectIndex" :?> TextBlock
  let kinectName = control.FindName "kinectName" :?> TextBox
  let kinectStatus = control.FindName "kinectStatus" :?> TextBlock
  let skeletonPanel = control.FindName "skeletonPanel" :?> StackPanel
  let skeletonCanvas = control.FindName "skeletonCanvas" :?> Canvas

  let jointColors = [
    (JointID.HipCenter, new SolidColorBrush(Color.FromRgb(169uy, 176uy, 155uy)))
    (JointID.Spine, new SolidColorBrush(Color.FromRgb(169uy, 176uy, 155uy)))
    (JointID.ShoulderCenter, new SolidColorBrush(Color.FromRgb(168uy, 230uy, 29uy)))
    (JointID.Head, new SolidColorBrush(Color.FromRgb(200uy, 0uy, 0uy)))
    (JointID.ShoulderLeft, new SolidColorBrush(Color.FromRgb(79uy, 84uy, 33uy)))
    (JointID.ElbowLeft, new SolidColorBrush(Color.FromRgb(84uy, 33uy, 42uy)))
    (JointID.WristLeft, new SolidColorBrush(Color.FromRgb(255uy, 126uy, 0uy)))
    (JointID.HandLeft, new SolidColorBrush(Color.FromRgb(215uy, 86uy, 0uy)))
    (JointID.ShoulderRight, new SolidColorBrush(Color.FromRgb(33uy, 79uy, 84uy)))
    (JointID.ElbowRight, new SolidColorBrush(Color.FromRgb(33uy, 33uy, 84uy)))
    (JointID.WristRight, new SolidColorBrush(Color.FromRgb(77uy, 109uy, 243uy)))
    (JointID.HandRight, new SolidColorBrush(Color.FromRgb(37uy, 69uy, 243uy)))
    (JointID.HipLeft, new SolidColorBrush(Color.FromRgb(77uy, 109uy, 243uy)))
    (JointID.KneeLeft, new SolidColorBrush(Color.FromRgb(69uy, 33uy, 84uy)))
    (JointID.AnkleLeft, new SolidColorBrush(Color.FromRgb(229uy, 170uy, 122uy)))
    (JointID.FootLeft, new SolidColorBrush(Color.FromRgb(255uy, 126uy, 0uy)))
    (JointID.HipRight, new SolidColorBrush(Color.FromRgb(181uy, 165uy, 213uy)))
    (JointID.KneeRight, new SolidColorBrush(Color.FromRgb(71uy, 222uy, 76uy)))
    (JointID.AnkleRight, new SolidColorBrush(Color.FromRgb(245uy, 228uy, 156uy)))
    (JointID.FootRight, new SolidColorBrush(Color.FromRgb(77uy,  109uy, 243uy))) ]

  let getDisplayPosition (joint:Joint) =
    match kinect with
    | Some k ->
      let mutable depthX = 0.0f
      let mutable depthY = 0.0f
      k.SkeletonEngine.SkeletonToDepthImage(joint.Position, &depthX, &depthY)
      let depthX = depthX * 320.0f
      let depthY = depthY * 240.0f
      let mutable colorX = 0
      let mutable colorY = 0
      let iv = new ImageViewArea()
      k.NuiCamera.GetColorPixelCoordinatesFromDepthPixel(ImageResolution.Resolution640x480, iv, int depthX, int depthY, 0s, &colorX, &colorY)
      new Point(skeletonCanvas.Width * (float colorX) / 640.0, skeletonCanvas.Height * (float colorY) / 480.0)
    | None -> new Point()

  let getBodySegment (joints : Microsoft.Research.Kinect.Nui.JointsCollection, brush : Brush, ids : JointID list) =
    let points = new PointCollection(ids.Length)
    ids |> List.iter (fun id -> points.Add(getDisplayPosition(joints.[id])))
    let polyline = new Polyline()
    polyline.Points <- points
    polyline.Stroke <- brush
    polyline.StrokeThickness <- 5.0
    polyline

  let mutable skeletonFrameReadyEvent : IDisposable option = None

  let initRuntime () =
    match kinect with
    | Some kinect ->
      match kinect.Status with
      | KinectStatus.Connected ->
        let skeletalViewerAvailable = this.IsSkeletalViewerAvailable
        runtimeOptions <-
          if skeletalViewerAvailable then RuntimeOptions.UseDepthAndPlayerIndex ||| RuntimeOptions.UseSkeletalTracking ||| RuntimeOptions.UseColor else RuntimeOptions.UseDepth ||| RuntimeOptions.UseColor
        kinect.Initialize(runtimeOptions)
        skeletonPanel.Visibility <- if skeletalViewerAvailable then System.Windows.Visibility.Visible else System.Windows.Visibility.Collapsed
        if runtimeOptions.HasFlag(RuntimeOptions.UseSkeletalTracking) then kinect.SkeletonEngine.TransformSmooth <- true
      | _ -> ()
    | None -> ()

  let createSkeletonFrameReady (runtime:Runtime) =
    runtime.SkeletonFrameReady
    |> Observable.subscribe begin
        fun e ->
          let skeletonFrame = e.SkeletonFrame

          if skeletonFrame = null then ()
          else
            let brushes : Brush array = Array.zeroCreate 6
            brushes.[0] <- new SolidColorBrush(Color.FromRgb(255uy, 0uy, 0uy)) :> Brush
            brushes.[1] <- new SolidColorBrush(Color.FromRgb(0uy, 255uy, 0uy)) :> Brush
            brushes.[2] <- new SolidColorBrush(Color.FromRgb(64uy, 255uy, 255uy)) :> Brush
            brushes.[3] <- new SolidColorBrush(Color.FromRgb(255uy, 255uy, 64uy)) :> Brush
            brushes.[4] <- new SolidColorBrush(Color.FromRgb(255uy, 64uy, 255uy)) :> Brush
            brushes.[5] <- new SolidColorBrush(Color.FromRgb(128uy, 128uy, 255uy)) :> Brush

            skeletonCanvas.Children.Clear()

            let rec updateJoints iSkeleton (skeletons:SkeletonData list) =
              match skeletons with
              | [] -> ()
              | x::xs ->
                match x.TrackingState with
                | SkeletonTrackingState.Tracked ->
                  let brush = brushes.[iSkeleton % brushes.Length];
                  skeletonCanvas.Children.Add(getBodySegment(x.Joints, brush, [JointID.HipCenter; JointID.Spine; JointID.ShoulderCenter; JointID.Head])) |> ignore
                  skeletonCanvas.Children.Add(getBodySegment(x.Joints, brush, [JointID.ShoulderCenter; JointID.ShoulderLeft; JointID.ElbowLeft; JointID.WristLeft; JointID.HandLeft])) |> ignore
                  skeletonCanvas.Children.Add(getBodySegment(x.Joints, brush, [JointID.ShoulderCenter; JointID.ShoulderRight; JointID.ElbowRight; JointID.WristRight; JointID.HandRight])) |> ignore
                  skeletonCanvas.Children.Add(getBodySegment(x.Joints, brush, [JointID.HipCenter; JointID.HipLeft; JointID.KneeLeft; JointID.AnkleLeft; JointID.FootLeft])) |> ignore
                  skeletonCanvas.Children.Add(getBodySegment(x.Joints, brush, [JointID.HipCenter; JointID.HipRight; JointID.KneeRight; JointID.AnkleRight; JointID.FootRight])) |> ignore

                  for j in x.Joints do
                    let joint = j :?> Joint
                    let jointPos = getDisplayPosition(joint)
                    let jointLine = new Line()
                    jointLine.X1 <- jointPos.X - 3.0
                    jointLine.X2 <- jointLine.X1 + 6.0
                    jointLine.Y1 <- jointPos.Y
                    jointLine.Y2 <- jointPos.Y
                    jointLine.Stroke <- jointColors |> List.find (fun (id, brush) -> id = joint.ID) |> fun (id,brush) -> brush
                    jointLine.StrokeThickness <- 6.0
                    skeletonCanvas.Children.Add(jointLine) |> ignore
                | _ -> ()
                xs |> updateJoints (iSkeleton + 1)

            skeletonFrame.Skeletons |> List.ofSeq |> updateJoints 0
      end
    |> Some

  member this.UpdateUi () =
    match kinect with
    | Some k ->
      kinectIndex.Text <- k.InstanceIndex.ToString()
      kinectName.Text <- k.InstanceName
      kinectStatus.Text <- k.Status.ToString()
    | None -> ()

  member this.IsSkeletalViewerAvailable =
    Microsoft.Research.Kinect.Nui.Runtime.Kinects
    |> Seq.forall (fun k -> k.SkeletonEngine = null)

  member this.Kinect
    with get() = kinect
    and set(k) = begin
      
      let unset = function
        | Some (old:Runtime) ->
          kinectColorViewer.Kinect <- None
          kinectDepthViewer.Kinect <- None
          match skeletonFrameReadyEvent with | Some e -> e.Dispose() | None -> ()
          old.Uninitialize()
        | None -> ()
      
      let trySet = function
        | Some current ->
          kinect <- k
          initRuntime()
          kinectColorViewer.Kinect <- kinect
          kinectDepthViewer.RuntimeOptions <- runtimeOptions
          kinectDepthViewer.Kinect <- kinect
          skeletonFrameReadyEvent <- current |> createSkeletonFrameReady
          this.UpdateUi()
        | None -> kinect <- None
      
      kinect |> unset
      k |> trySet
    end

  member this.RuntimeOptions with get() = runtimeOptions and set(o) = runtimeOptions <- o

  member this.ReInitRuntime () = kinect <- kinect

  member this.KinectColorViewer = kinectColorViewer

  member this.KinectDepthViewer = kinectDepthViewer

  member this.Control = control