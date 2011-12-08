namespace Microsoft.Samples.Kinect.WpfViewers

open System
open System.Windows
open System.ComponentModel
open System.Windows.Controls
open Microsoft.Research.Kinect.Nui

type KinectDepthViewer() as this =

  let redIndex = 2
  let greenIndex = 1
  let blueIndex = 0

  let mutable frameRate = -1;
  let mutable totalFrames = 0
  let mutable lastFrames = 0
  let mutable lastTime = DateTime.MaxValue

  let mutable kinect : Runtime option = None

  let mutable runtimeOptions : RuntimeOptions = RuntimeOptions.UseDepthAndPlayerIndex

  let mutable imageHelper : InteropBitmapHelper option = None

  let depthFrame32 : byte array = Array.zeroCreate (320 * 240 * 4)

  let control = 
    Application.LoadComponent(new System.Uri("/Microsoft.Samples.Kinect.WpfViewers;component/KinectDepthViewer.xaml", System.UriKind.Relative)) :?> UserControl

  let kinectDepthImage = control.FindName "kinectDepthImage" :?> Image
  
  let mutable depthImageReady : IDisposable option = None

  let propertyChanged = Event<_,_>()

  let calculateFrameRate () =
    totalFrames <- totalFrames + 1
    let cur = DateTime.Now
    if lastTime = DateTime.MaxValue || cur.Subtract(lastTime) > TimeSpan.FromSeconds(float 1.) then
      this.FrameRate <- totalFrames - lastFrames
      lastFrames <- totalFrames
      lastTime <- cur

  let convertDepthFrame (depthFrame16:byte array) =

    let hasPlayerData = runtimeOptions.HasFlag(RuntimeOptions.UseDepthAndPlayerIndex)

    let rec convert (i16:int) (i32:int) =
      if i16 < depthFrame16.Length && i32 < depthFrame32.Length then
        let player = if hasPlayerData then byte <| depthFrame16.[i16] &&& 0x07uy else byte <| -1
        let realDepth =
          if hasPlayerData then (depthFrame16.[i16 + 1] <<< 5) ||| (depthFrame16.[i16] >>> 3)
          else (depthFrame16.[i16 + 1] <<< 8) ||| (depthFrame16.[i16])
        let intensity = byte (255 - (255 * (int <| realDepth) / 0x0fff))

        depthFrame32.[i32 + redIndex] <- 0uy
        depthFrame32.[i32 + greenIndex] <- 0uy
        depthFrame32.[i32 + blueIndex] <- 0uy

        match int player with
        | -1 | 0 ->
          depthFrame32.[i32 + redIndex] <- (intensity / 2uy)
          depthFrame32.[i32 + greenIndex] <- (intensity / 2uy)
          depthFrame32.[i32 + blueIndex] <- (intensity / 2uy)
        | 1 -> depthFrame32.[i32 + redIndex] <- intensity
        | 2 -> depthFrame32.[i32 + greenIndex] <- intensity
        | 3 ->
          depthFrame32.[i32 + redIndex] <- (intensity / 4uy)
          depthFrame32.[i32 + greenIndex] <- intensity
          depthFrame32.[i32 + blueIndex] <- intensity
        | 4 ->
          depthFrame32.[i32 + redIndex] <- intensity
          depthFrame32.[i32 + greenIndex] <- intensity
          depthFrame32.[i32 + blueIndex] <- (intensity / 4uy)
        | 5 -> 
          depthFrame32.[i32 + redIndex] <- (intensity)
          depthFrame32.[i32 + greenIndex] <- (intensity / 4uy)
          depthFrame32.[i32 + blueIndex] <- intensity
        | 6 ->
          depthFrame32.[i32 + redIndex] <- (intensity / 2uy)
          depthFrame32.[i32 + greenIndex] <- (intensity / 2uy)
          depthFrame32.[i32 + blueIndex] <- intensity
        | 7 ->
          depthFrame32.[i32 + redIndex] <- (255uy - intensity)
          depthFrame32.[i32 + greenIndex] <- (255uy - intensity)
          depthFrame32.[i32 + blueIndex] <- (255uy - intensity)
        | _ -> ()

        convert (i16 + 2) (i32 + 4)

    convert 0 0
    depthFrame32

  let createDepthImageReady (runtime:Runtime) =
    runtime.DepthFrameReady
    |> Observable.subscribe begin
        fun e ->
          let planarImage = e.ImageFrame.Image
          let convertedDepthBits = convertDepthFrame(planarImage.Bits)
          match imageHelper with
          | None ->
            let helper = new InteropBitmapHelper(planarImage.Width, planarImage.Height, convertedDepthBits)
            imageHelper <- Some helper
            kinectDepthImage.Source <- helper.InteropBitmap
          | Some helper ->
            helper.UpdateBits(convertedDepthBits)
            
          calculateFrameRate()
      end
    |> Some

  member this.Kinect
    with get() = kinect
    and set(k) = begin

      let unset = function
        | Some _ ->
          match depthImageReady with | Some e -> e.Dispose(); depthImageReady <- None | None -> ()
        | None -> ()

      let trySet = function
        | Some (current:Runtime) ->
          kinect <- k
          match current.Status with
          | KinectStatus.Connected ->
            lastTime <- DateTime.MaxValue
            totalFrames <- 0
            lastFrames <- 0
            current.DepthStream.Open(ImageStreamType.Depth, 2, ImageResolution.Resolution320x240,
              if runtimeOptions.HasFlag(RuntimeOptions.UseDepthAndPlayerIndex) || runtimeOptions.HasFlag(RuntimeOptions.UseSkeletalTracking) then ImageType.DepthAndPlayerIndex else ImageType.Depth)
            depthImageReady <- current |> createDepthImageReady
          | _ -> ()
        | None -> kinect <- None

      kinect |> unset
      k |> trySet
    end

  member this.RuntimeOptions with get() = runtimeOptions and set(o) = runtimeOptions <- o

  interface INotifyPropertyChanged with
    [<CLIEvent>]
    member this.PropertyChanged = propertyChanged.Publish

  member this.FrameRate
    with get() = frameRate
    and set(value) =
      if frameRate <> value then
        frameRate <- value
        propertyChanged.Trigger(this, PropertyChangedEventArgs("FrameRate"))

  member this.Control = control