// RTCore551 - Task 1: DRAFT host-side UART link. The framing below is a proposal for the 11/19 "interface with UART"
// milestone; the RTL owners should change it freely, this file only exists so the scene export has a first consumer.
//
// Proposed protocol (little-endian):
//   PC  -> FPGA : "RTGO" | u32 payloadLength | payload (= the legacy RTS1 .rtscene file, CRC32 included)
//   FPGA -> PC  : "RTFR" | u16 width | u16 height | width*height*3 bytes RGB, row-major from the top-left pixel
//
// THIS FILE IS SWITCHED OFF BY DEFAULT because System.IO.Ports does not exist in new Unity projects and the whole
// project would fail to compile. To use it:
//   1. Edit > Project Settings > Player > Other Settings > Api Compatibility Level = ".NET Framework"
//   2. Same page, "Scripting Define Symbols": add  RTCORE_UART
#if RTCORE_UART && !UNITY_WEBGL
using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using UnityEngine;

namespace RTCore
{
    public class UartLink : IDisposable
    {
        private readonly SerialPort port;

        public UartLink(string portName, int baud = 921600, int timeoutMs = 60000)
        {
            port = new SerialPort(portName, baud, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = timeoutMs,
                WriteTimeout = timeoutMs
            };
            port.Open();
        }

        public static string[] AvailablePorts() { return SerialPort.GetPortNames(); }

        public void SendScene(byte[] rtsceneFile)
        {
            byte[] header = new byte[8];
            Encoding.ASCII.GetBytes("RTGO").CopyTo(header, 0);
            BitConverter.GetBytes((uint)rtsceneFile.Length).CopyTo(header, 4);   // BitConverter is little-endian on x86/ARM
            port.Write(header, 0, header.Length);
            port.Write(rtsceneFile, 0, rtsceneFile.Length);
        }

        /// <summary>Blocks until a full frame arrives. Returns RGB24 bytes, top-left first.</summary>
        public byte[] ReceiveFrame(out int width, out int height)
        {
            byte[] head = ReadExactly(8);
            if (Encoding.ASCII.GetString(head, 0, 4) != "RTFR")
                throw new InvalidDataException("Bad frame marker from FPGA.");
            width = BitConverter.ToUInt16(head, 4);
            height = BitConverter.ToUInt16(head, 6);
            return ReadExactly(width * height * 3);
        }

        public static void SaveRgbAsPng(byte[] rgb, int width, int height, string path)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            var px = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int src = (y * width + x) * 3;
                    px[(height - 1 - y) * width + x] = new Color32(rgb[src], rgb[src + 1], rgb[src + 2], 255);   // Unity textures start bottom-left
                }
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        private byte[] ReadExactly(int count)
        {
            var buf = new byte[count];
            int got = 0;
            while (got < count) got += port.Read(buf, got, count - got);
            return buf;
        }

        public void Dispose()
        {
            if (port != null && port.IsOpen) port.Close();
        }
    }
}
#endif
