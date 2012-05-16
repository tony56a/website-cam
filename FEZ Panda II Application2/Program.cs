
using System;
using System.IO;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.IO;
using System.IO.Ports;
using GHIElectronics.NETMF.IO;
using Microsoft.SPOT.Hardware;
using GHIElectronics.NETMF.FEZ;
using GHIElectronics.NETMF.Net;
using GHIElectronics.NETMF.Net.Sockets;
using GHIElectronics.NETMF.Net.NetworkInformation;
using System.Text;
using GHIElectronics.NETMF.Hardware;
using Socket = GHIElectronics.NETMF.Net.Sockets.Socket;

namespace FEZ_Panda_II_Application2
{
    
    public class Program
    {
       static PersistentStorage sdCard = new PersistentStorage("SD");
       static Boolean pictureTaken = true;
         public static void takePicture()
         {
             Debug.GC(true);
             Debug.EnableGCMessages(false);
             string file = string.Empty;
             FileStream fs = null;
             if (VolumeInfo.GetVolumes()[0].IsFormatted)
             {
                 file = VolumeInfo.GetVolumes()[0].RootDirectory + "\\" + "testtemp.jpg";
                 fs = new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite);
             }
             var camera = new UartCamera(new SerialPort("COM1", 115200),fs);
             Debug.Print(camera.setPictureSize(UartCamera.SET_SIZE_320x240).ToString());
             if (camera.reset())
             {
                 Debug.Print("Writing");
                 pictureTaken = false;
                 lock (fs)
                 {
                     camera.getPicture();
                     pictureTaken = true;
                 }
             }
             Debug.GC(true);
             fs.Close();
             
         }

        public static void Main()
        {
            const Int32 c_port = 80;
            byte[] ip = { 192, 168, 1, 222 };
            byte[] subnet = { 255, 255, 255, 0 };
            byte[] gateway = { 192, 168, 1, 1 };
            byte[] mac = { 0x00, 0x88, 0x98, 0x90, 0xD4, 0xE0 };
            WIZnet_W5100.Enable(SPI.SPI_module.SPI1, (Cpu.Pin)FEZ_Pin.Digital.Di10,
                                                (Cpu.Pin)FEZ_Pin.Digital.Di7, true);
            NetworkInterface.EnableStaticIP(ip, subnet, gateway, mac);
            NetworkInterface.EnableStaticDns(new byte[] { 192, 168, 1, 1 });
            HttpListener listener = new HttpListener("http", c_port);
            String file = string.Empty;
            sdCard.MountFileSystem();
            Debug.EnableGCMessages(false);
            listener.Start();

            while (true)
            {
                HttpListenerResponse response = null;
                HttpListenerContext context = null;
                HttpListenerRequest request = null;
                try
                {

                    context = listener.GetContext();
                    request = context.Request;
                    response = context.Response;
                    response.StatusCode = (int)HttpStatusCode.OK;
                    Debug.Print(request.RawUrl);
                    Debug.Print(request.HttpMethod);
                    if (request.RawUrl.Length > 1)
                    {
                        if (request.HttpMethod == "GET")
                        {
                            
                            if (request.RawUrl =="/testtemp.jpg")
                            {
                                if (pictureTaken)
                                {
                                    Debug.GC(true);
                                    sendFile(request, response);
                                }
                            }
                            else
                            {
                                Debug.GC(true);
                                sendFile(request, response);
                            }
                        }
                        else if (request.HttpMethod == "POST")
                        {

                            if (request.RawUrl.CompareTo("/getPicture") == 0)
                            {
                                if (pictureTaken)
                                {
                                    Thread thread = new Thread(takePicture);
                                    thread.Start();
                                    response.OutputStream.Write(Encoding.UTF8.GetBytes("Picture Taken"), 0, 13);
                                }
                                else
                                {
                                    Byte[] aString = Encoding.UTF8.GetBytes("Picture already being taken");
                                    response.OutputStream.Write(aString, 0, aString.Length);
                                }
                            }
                        }
                    }
                    else
                    {
                        string rootDirectory =
                        VolumeInfo.GetVolumes()[0].RootDirectory;
                        string[] files = Directory.GetFiles(rootDirectory);
                        for (int i = 0; i < files.Length; i++)
                        {
                            response.OutputStream.Write(Encoding.UTF8.GetBytes(files[i]), 0, files[i].Length);
                            response.OutputStream.WriteByte(10);
                        }
                        string[] folders = Directory.GetDirectories(rootDirectory);
                        for (int i = 0; i < folders.Length; i++)
                        {
                            Debug.Print(folders[i]);
                            string[] subFiles = Directory.GetFiles(folders[i]);
                            for (int j = 0; j < subFiles.Length; j++)
                            {
                                response.OutputStream.Write(Encoding.UTF8.GetBytes(subFiles[j]), 0, subFiles[j].Length);
                                response.OutputStream.WriteByte(10);
                            }
                        }


                    }
                    //We are ignoring the request, assuming GET
                    // Sends response:           
                    response.Close();
                    context.Close();
                }
                catch
                {
                    if (context != null)
                    {
                        context.Close();
                    }
                }
            }
        }
        private static String formatUrl(String url)
        {
            return "\\" + url.Substring(1);
        }


        private static void sendFile(HttpListenerRequest request,HttpListenerResponse response)
        {
            String file = VolumeInfo.GetVolumes()[0].RootDirectory + formatUrl(request.RawUrl);
            FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read);
            byte[] page = new byte[1024];
            while (fs.Read(page, 0, 1024) > 0)
            {
                response.OutputStream.Write(page, 0, page.Length);
                Array.Clear(page, 0, 1024);
                Debug.GC(true);
            }
            fs.Close();
        }
    }

}