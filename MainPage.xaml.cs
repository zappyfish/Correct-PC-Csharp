using System;
using System.IO;
using Windows.UI.Xaml.Controls;
using System.Diagnostics;
using Windows.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.Graphics.Imaging;


namespace Get_Images_Test
{
    public sealed partial class MainPage : Page
    {
        static string ServerPortNumber = "5000";

        static int PACKET_SIZE = 1000;

        const char START_RGB = 'M';
        const char START_ONE_BAND = 'I';

        const int AWAITING_IMAGE = 0;
        const int RECEIVING_RGB = 1;
        const int RECEIVING_ONE_BAND = 2;

        int ReceivingStatus = AWAITING_IMAGE;
        byte[] ImageBuffer = null;
        int ImgCount = 0;

        Windows.Networking.Sockets.DatagramSocket serverDatagramSocket = null;

        BitmapImage bitmapImage;
    
        public MainPage()
        {
            bitmapImage = new BitmapImage();
            this.InitializeComponent();
            this.StartServer();
        }


        private async void StartServer()
        {
            try
            {
                serverDatagramSocket = new Windows.Networking.Sockets.DatagramSocket();

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

        private void ServerDatagramSocket_MessageReceived(Windows.Networking.Sockets.DatagramSocket sender, Windows.Networking.Sockets.DatagramSocketMessageReceivedEventArgs args)
        {
            Stream streamIn = args.GetDataStream().AsStreamForRead();
            MemoryStream ms = ToMemoryStream(streamIn);
            byte[] data = ms.ToArray();
            switch (ReceivingStatus)
            {
                case AWAITING_IMAGE:
                    if (data.Length == 3)
                    {
                        StartReceivingImage(data);
                    }
                    break;
                default:
                    if (data.Length == 0)
                    {
                        // Handle image here 
                        serverDatagramSocket.MessageReceived -= ServerDatagramSocket_MessageReceived;
                        ReceivingStatus = AWAITING_IMAGE;
                        byte[] processingBuffer = ImageBuffer;
                        ImageBuffer = null;
                        DisplayImage(processingBuffer);
                        ImgCount++;
                        // showImage = false;
                        Debug.WriteLine(ImgCount);
                    }
                    // It's only possible for start packets to be of size 3 (others must be >= 4)
                    else if (data.Length == 3)
                    {
                        StartReceivingImage(data);
                    }
                    else
                    {
                        int ind = data[0];
                        ind |= ((data[1] & 0xff) << 8);
                        ind |= ((data[2] & 0xff) << 16);
                        for (int i = 0; i < data.Length - 3; ++i)
                        {
                            ImageBuffer[(ind * PACKET_SIZE) + i] = data[i + 3];
                        }
                    }
                    break;
            } 
        }
      

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
                ReceivingStatus = START_ONE_BAND;
                ImageBuffer = new byte[PACKET_SIZE * num_packets];
            }
        }

        private async void DisplayImage(byte[] img)
        {
            /*
            var jpgImage = new Byte[img.Count * PACKET_SIZE];
            for (int i = 0; i < img.Count; ++i)
            {
                for (int j = 0; j < img[i].Length; ++j)
                {
                    jpgImage[(i * PACKET_SIZE) + j] = img[i][j];
                }
            }
            */

            try
            {

                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(ConvertTo(img));
                SoftwareBitmap sftwareBmp = await decoder.GetSoftwareBitmapAsync();
                SoftwareBitmap displayableImage = SoftwareBitmap.Convert(sftwareBmp, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);


                // var data = await decoder.GetPixelDataAsync();
                // var bytes = data.DetachPixelData();

                await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                async () =>
                {
                    var source = new SoftwareBitmapSource();
                    await source.SetBitmapAsync(displayableImage);
                    testImg.Source = source;
                }
                );
                Debug.WriteLine("Displayed!");
            } catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
            serverDatagramSocket.MessageReceived += ServerDatagramSocket_MessageReceived;
        }

        internal static IRandomAccessStream ConvertTo(byte[] arr)
        {
            MemoryStream stream = new MemoryStream(arr);
            return stream.AsRandomAccessStream();
        }

        public static void CopyStream(Stream input, Stream output)
        {
            byte[] buffer = new byte[8 * 1024];
            int len;
            while ((len = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, len);
            }
        }

        static MemoryStream ToMemoryStream(Stream input)
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

