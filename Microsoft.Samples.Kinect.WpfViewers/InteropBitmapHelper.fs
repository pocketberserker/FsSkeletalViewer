namespace Microsoft.Samples.Kinect.WpfViewers

open System
open System.Runtime.InteropServices
open System.Windows.Interop
open System.Windows.Media

type InteropBitmapHelper(width:int , height:int , imageBits:byte array, pixelFormat:PixelFormat) =
  
  [<DllImport("kernel32.dll", SetLastError = true)>]
  static extern IntPtr CreateFileMapping(IntPtr hFile, IntPtr lpFileMappingAttributes, uint32 flProtect, uint32 dwMaximumSizeHigh, uint32 dwMaximumSizeLow, string lpName)

  [<DllImport("kernel32.dll", SetLastError = true)>]
  static extern IntPtr MapViewOfFile(IntPtr hFileMappingObject, uint32 dwDesiredAccess, uint32 dwFileOffsetHigh, uint32 dwFileOffsetLow, uint32 dwNumberOfBytesToMap)

  let stride = width * pixelFormat.BitsPerPixel / 8

  let colorByteCount = height * stride |> uint32

  let colorFileMapping = CreateFileMapping(new IntPtr(-1), IntPtr.Zero, 0x04u, 0u, colorByteCount, null)

  let imageBits_ = MapViewOfFile(colorFileMapping, 0xF001Fu, 0u, 0u, colorByteCount)

  let mutable interopBitmap =
    Marshal.Copy(imageBits, 0, imageBits_, colorByteCount |> int)
    Imaging.CreateBitmapSourceFromMemorySection(colorFileMapping, width, height, pixelFormat, stride, 0) :?> InteropBitmap

  new(width ,height , imageBits) = InteropBitmapHelper(width,height,imageBits,PixelFormats.Bgr32)

  member this.UpdateBits (imageBits:byte array) =
    Marshal.Copy(imageBits, 0, imageBits_, colorByteCount |> int);
    interopBitmap.Invalidate()

  member this.InteropBitmap with get() = interopBitmap and private set(i) = interopBitmap <- i