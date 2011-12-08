namespace Microsoft.Samples.Kinect.WpfViewers

open System
open System.Windows
open System.Windows.Controls
open Microsoft.Research.Kinect.Nui

type KinectColorViewer() =

  let mutable kinect : Runtime option = None

  let mutable runtimeOptions : RuntimeOptions = RuntimeOptions.UseColor

  let mutable imageHelper : InteropBitmapHelper option = None

  let control = 
    Application.LoadComponent(new System.Uri("/Microsoft.Samples.Kinect.WpfViewers;component/KinectColorViewer.xaml", System.UriKind.Relative)) :?> UserControl

  let kinectColorImage = control.FindName "kinectColorImage" :?> Image

  let mutable colorImageReady : IDisposable option = None

  let createColorImageReady (runtime:Runtime) =
    runtime.VideoFrameReady
    |> Observable.subscribe begin
        fun e ->
          let planarImage = e.ImageFrame.Image
          match imageHelper with
          | None ->
            new InteropBitmapHelper(planarImage.Width, planarImage.Height, planarImage.Bits)
            |> fun helper -> imageHelper <- Some helper; helper
            |> fun helper -> kinectColorImage.Source <- helper.InteropBitmap
          | Some helper ->
            helper.UpdateBits(planarImage.Bits)
      end
    |> Some

  member this.Kinect
    with get() = kinect
    and set(k) = begin

      let unset = function
        | Some _ ->
          match colorImageReady with | Some e -> e.Dispose(); colorImageReady <- None | None -> ()
        | None -> ()
      
      let trySet = function
        | Some (current:Runtime) ->
          kinect <- k
          match current.Status with
          | KinectStatus.Connected ->
            current.VideoStream.Open(ImageStreamType.Video, 2, ImageResolution.Resolution640x480, ImageType.Color)
            colorImageReady <- current |> createColorImageReady
          | _ -> ()
        | None -> kinect <- None
      
      kinect |> unset
      k |> trySet
    end

  member this.RuntimeOptions with get() = runtimeOptions and set(o) = runtimeOptions <- o

  member this.Control = control