using System;
using System.IO;
using System.Text;
using Microsoft.SPOT;
using System.IO.Ports;
using System.Threading;





namespace FEZ_Panda_II_Application2
{
    public class UartCamera : IDisposable
    {
        FileStream fs = null;
        public UartCamera(SerialPort port,FileStream fs)
        {
            this.port = port;
            this.fs = fs;
            port.ReadTimeout = 250; //so read call doesn't block forever
            if (!port.IsOpen)
            {
                port.Open();
            }
        }

        public bool reset()
        {
            bool sendAndLookFor = SendAndLookFor(RESET_COMMAND, RESET_OK_RESPONSE);

            //camera needs time after reset
            if (sendAndLookFor)
            {
                ReadAllRemaining();
                Thread.Sleep(3000);
            }

            return sendAndLookFor;
        }

        public bool setPictureSize(byte[] sizeBytes)
        {
            bool sendAndLookFor = SendAndLookFor(sizeBytes, SET_SIZE_OK_RESPONSE);
            if (sendAndLookFor)
                ReadAllRemaining();

            return sendAndLookFor;
        }

        public bool stop()
        {
            ReadAllRemaining();
            return SendAndLookFor(STOP_COMMAND, STOP_OK_RESPONSE);
        }

        public delegate void Complete();

        const int IN_BUFFER_SIZE = 512;
        byte[] InBuffer = new byte[IN_BUFFER_SIZE];

        public void getPicture()
        {
            Send(SNAP_COMMAND);
            if (LookFor(SNAP_OK_RESPONSE))
            {
                Send(SIZE_COMMAND);
                if (LookFor(SIZE_OK_RESPONSE))
                {
                    //MSB, LSB
                    var sizeBytesLength = Read(2);
                    int fileSize = (InBuffer[0] << 8) | InBuffer[1];

                    int startAddress = 0;
                    int bytesRead = 0;

                    GET_CHUNK_COMMAND[12] = MSB(IN_BUFFER_SIZE);
                    GET_CHUNK_COMMAND[13] = LSB(IN_BUFFER_SIZE);

                    bool endReached = false;
                    while (!endReached)
                    {
                        GET_CHUNK_COMMAND[8] = MSB(startAddress);
                        GET_CHUNK_COMMAND[9] = LSB(startAddress);

                        Send(GET_CHUNK_COMMAND);
                        if (LookFor(GET_CHUNK_OK_RESPONSE))
                        {
                            int chunkLength = 0;
                            do
                            {
                                chunkLength = Read();

                                //ditch footer
                                Read(junkBuffer, GET_CHUNK_OK_RESPONSE.Length);

                                //publish byte data
                                if (chunkLength > 0)
                                {
                                    bytesRead += chunkLength;
                                    if (bytesRead >= fileSize)
                                    {
                                        endReached = true;

                                        chunkLength = FindEnd(chunkLength);
                                    }
                                    Debug.Print("Writing Bytes");
                                    fs.Write(NewArray(chunkLength),0,chunkLength);
                                }

                                startAddress += chunkLength;

                            } while (!endReached && chunkLength > 0);
                        }
                    }
                }
            }
            port.Close();
            Debug.GC(true);
            Debug.Print("Done");
        }

        private byte[] NewArray(int chunkLength)
        {
            //make new array for bytes event so receiver can consume
            //in sep thread without it changing during processing
            var chunk = new byte[chunkLength];
            Array.Copy(InBuffer, chunk, chunkLength);
            return chunk;
        }

        private int FindEnd(int chunkLength)
        {
            if (chunkLength >= 2)
            {
                bool foundEnd = false;

                for (int i = chunkLength - 1; i >= 2; i--)
                {
                    if (InBuffer[i - 1] == 0xFF &&
                        InBuffer[i - 0] == 0xD9
                    )
                    {
                        chunkLength = i + 1; //include end marker in output
                        foundEnd = true;
                        break;
                    }
                }

                if (!foundEnd)
                    Debug.Print("Invalid JPG data");
            }
            return chunkLength;
        }

        private static byte LSB(int num)
        {
            return (byte)(num & 0xFF);
        }

        private static byte MSB(int num)
        {
            return (byte)(num >> 8);
        }

        private bool SendAndLookFor(byte[] command, byte[] lookFor)
        {
            Send(command);

            return LookFor(lookFor);
        }

        byte[] junkBuffer = new byte[IN_BUFFER_SIZE];
        private void ReadAllRemaining()
        {
            int readCount = 0;

            do
            {
                readCount = Read(junkBuffer, IN_BUFFER_SIZE);
            } while (readCount != 0);
        }

        private bool LookFor(byte[] expectedResponse)
        {
            var inSize = Read(expectedResponse.Length);
            if (AreEqual(expectedResponse, inSize))
                return true;

            return false;
        }

        private int Read()
        {
            return Read(IN_BUFFER_SIZE);
        }

        private int Read(int bytes)
        {
            return Read(InBuffer, bytes);
        }

        private int Read(byte[] buffer, int bytes)
        {
            return port.Read(buffer, 0, bytes);
        }

        private void Send(byte[] command)
        {
            port.Write(command, 0, command.Length);
        }

        static readonly byte[] RESET_OK_RESPONSE = new byte[] { 0x76, 0x00, 0x26, 0x00 };
        static readonly byte[] RESET_COMMAND = new byte[] { 0x56, 0x00, 0x26, 0x00 };

        static readonly byte[] STOP_OK_RESPONSE = new byte[] { 0x76, 0x00, 0x36, 0x00, 0x00 };
        static readonly byte[] STOP_COMMAND = new byte[] { 0x56, 0x00, 0x36, 0x01, 0x03 };

        static readonly byte[] SNAP_OK_RESPONSE = new byte[] { 0x76, 0x00, 0x36, 0x00, 0x00 };
        static readonly byte[] SNAP_COMMAND = new byte[] { 0x56, 0x00, 0x36, 0x01, 0x00 };

        static readonly byte[] SIZE_OK_RESPONSE = new byte[] { 0x76, 0x00, 0x34, 0x00, 0x04, 0x00, 0x00 };
        static readonly byte[] SIZE_COMMAND = new byte[] { 0x56, 0x00, 0x34, 0x01, 0x00 };

        static readonly byte[] GET_CHUNK_OK_RESPONSE = new byte[] { 0x76, 0x00, 0x32, 0x00, 0x00 };
        static readonly byte[] GET_CHUNK_COMMAND = new byte[] { 0x56, 0x00, 0x32, 0x0C, 0x00, 0x0A, 0x00, 0x00, 255, 255, 0x00, 0x00, 255, 255, 0x00, 0x0A };

        public static byte[] SET_SIZE_160x120 = new byte[] { 0x56, 0x00, 0x31, 0x05, 0x04, 0x01, 0x00, 0x19, 0x22 };
        public static byte[] SET_SIZE_320x240 = new byte[] { 0x56, 0x00, 0x31, 0x05, 0x04, 0x01, 0x00, 0x19, 0x11 };
        public static byte[] SET_SIZE_640x480 = new byte[] { 0x56, 0x00, 0x31, 0x05, 0x04, 0x01, 0x00, 0x19, 0x00 };
        public static byte[] SET_SIZE_OK_RESPONSE = new byte[] { 0x76, 0, 0x31, 0 };

        SerialPort port;

        private bool AreEqual(byte[] left, int inSize)
        {
            if (left == null || left.Length != inSize)
                return false;

            for (int i = 0; i < left.Length; i++)
                if (left[i] != InBuffer[i])
                    return false;

            return true;
        }

        #region IDisposable Members

        public void Dispose()
        {
            if (port != null)
                port.Dispose();
        }

        #endregion
    }

   
    }

