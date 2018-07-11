using System;
using System.IO;
using System.Diagnostics;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;

namespace Receiving
{
    class ImageReceiver
    {
        static string ServerPortNumber = "5000";

        private static int PACKET_SIZE = 1000;
        private static int NORMAL_PACKET_INDEX_BYTES = 3;
        private static int START_PACKET_SIZE = 3;

        private const char START_RGB = 'M';
        private const char START_ONE_BAND = 'I';

        private const int AWAITING_IMAGE = 0;
        private const int RECEIVING_RGB = 1;
        private const int RECEIVING_ONE_BAND = 2;

        private int ReceivingStatus = AWAITING_IMAGE;
        private byte[] ImageBuffer = null;

        private ImageDisplayer Displayer = null;

        // The singleton instance
        private static ImageReceiver sReceiver;

        public static ImageReceiver GetReceiver()
        {
            if (sReceiver == null)
            {
                sReceiver = new ImageReceiver();
            }
            return sReceiver;
        }

        public async void StartServer()
        {
            try
            {
                var serverDatagramSocket = new Windows.Networking.Sockets.DatagramSocket();

                // The ConnectionReceived event is raised when connections are received.
                serverDatagramSocket.MessageReceived += ServerDatagramSocket_MessageReceived;

                // Start listening for incoming TCP connections on the specified port. You can specify any port that's not currently in use.
                await serverDatagramSocket.BindServiceNameAsync(ServerPortNumber);

            }
            catch (Exception ex)
            {
                Windows.Networking.Sockets.SocketErrorStatus webErrorStatus = Windows.Networking.Sockets.SocketError.GetStatus(ex.GetBaseException().HResult);
                Debug.WriteLine("failed to bind");
            }

        }

        public void StopServer()
        {
            // TODO: Complete me (not high priority)
        }

        public void SetDisplayer(ImageDisplayer NewDisplayer)
        {
            Displayer = NewDisplayer;
        }

        private void ServerDatagramSocket_MessageReceived(Windows.Networking.Sockets.DatagramSocket sender, Windows.Networking.Sockets.DatagramSocketMessageReceivedEventArgs args)
        {
            Stream streamIn = args.GetDataStream().AsStreamForRead();
            MemoryStream ms = ToMemoryStream(streamIn);
            byte[] data = ms.ToArray();
            switch (ReceivingStatus)
            {
                case AWAITING_IMAGE:
                    if (data.Length == START_PACKET_SIZE)
                    {
                        StartReceivingImage(data);
                    }
                    break;
                default:
                    if (data.Length == 0)
                    {
                        // Handle image here 
                        byte[] processingBuffer = ImageBuffer;
                        ImageBuffer = null;
                        int imageType = ReceivingStatus;
                        ReceivingStatus = AWAITING_IMAGE;
                        // TODO: Launch DisplayImage in a new thread
                        DisplayImage(processingBuffer, imageType);
                    }
                    // It's only possible for start packets to be of size 3 (others must be >= 4)
                    else if (data.Length == START_PACKET_SIZE)
                    {
                        StartReceivingImage(data);
                    }
                    else
                    {
                        int ind = data[0];
                        ind |= ((data[1] & 0xff) << 8);
                        ind |= ((data[2] & 0xff) << 16);
                        for (int i = 0; i < data.Length - NORMAL_PACKET_INDEX_BYTES; ++i)
                        {
                            ImageBuffer[(ind * PACKET_SIZE) + i] = data[i + NORMAL_PACKET_INDEX_BYTES];
                        }
                    }
                    break;
            }
        }

        // For the first udp packet of an image, the first byte is either an 'M' for RGB, or an 'I' for one band. The next two bytes indicate how many udp packets make up the image.
        private void StartReceivingImage(byte[] data)
        {
            char nextByte = (char)data[0];
            if (nextByte == START_RGB)
            {
                UInt16 num_packets = data[1];
                num_packets |= (UInt16)((data[2] & 0xff) << 8);
                ReceivingStatus = RECEIVING_RGB;
                ImageBuffer = new byte[PACKET_SIZE * num_packets];
            }
            else if (nextByte == START_ONE_BAND)
            {
                UInt16 num_packets = data[1];
                num_packets |= (UInt16)((data[2] & 0xff) << 8);
                ReceivingStatus = RECEIVING_ONE_BAND;
                ImageBuffer = new byte[PACKET_SIZE * num_packets];
            }
        }

        private async void DisplayImage(byte[] img, int imageType)
        {
            if (Displayer != null)
            {
                try
                {
                    // Decode the JPEG
                    BitmapDecoder decoder = await BitmapDecoder.CreateAsync(ConvertTo(img));
                    PixelDataProvider pixelData = await decoder.GetPixelDataAsync();
                    byte[] decompressedImage = pixelData.DetachPixelData();
                    uint width = decoder.PixelWidth;
                    uint height = decoder.PixelHeight;
                    // TODO: Convert SoftwareBitmap to matrix and pass to ImageDisplayer
                    if (imageType == RECEIVING_RGB)
                    {
                        // TODO: Make Display methods take the one dimensional decompressed byte array
                        byte[,,] decompressedRGBMatrix = ConvertDecompressedToRGBMatrix(decompressedImage, width, height);
                        Displayer.DisplayRGB(decompressedImage);
                    }
                    else if (imageType == RECEIVING_ONE_BAND)
                    {
                        // Displayer.DisplayOneBand(decompressedImage);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }
        }

        private static byte[,,] ConvertDecompressedToRGBMatrix(byte[] decompressed, uint width, uint height)
        {
            byte[,,] rgbMatrix = new byte[width, height, 3];
            for (int row = 0; row < height; ++row)
            {
                for (int col = 0; col < width; ++col)
                {
                    for (int band = 0; band < 3; ++band)
                    {
                        rgbMatrix[row, col, band] = decompressed[band + (3 * col) + (3 * width * row)]; 
                    }
                }
            }
            return rgbMatrix;
        }

        private static IRandomAccessStream ConvertTo(byte[] arr)
        {
            MemoryStream stream = new MemoryStream(arr);
            return stream.AsRandomAccessStream();
        }

        private static void CopyStream(Stream input, Stream output)
        {
            byte[] buffer = new byte[8 * 1024];
            int len;
            while ((len = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, len);
            }
        }

        private static MemoryStream ToMemoryStream(Stream input)
        {
            try
            {                                         // Read and write in
                byte[] block = new byte[0x1000];       // blocks of 4K.
                MemoryStream ms = new MemoryStream();
                while (true)
                {
                    int bytesRead = input.Read(block, 0, block.Length);
                    if (bytesRead == 0) return ms;
                    ms.Write(block, 0, bytesRead);
                }
            }
            finally { }
        }
    }
}
