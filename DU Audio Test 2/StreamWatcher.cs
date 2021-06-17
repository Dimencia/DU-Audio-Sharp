using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DU_Audio_Test_2
{
    public delegate void MessageAvailableEventHandler(object sender,
    MessageAvailableEventArgs e);

    public class MessageAvailableEventArgs : EventArgs
    {
        public MessageAvailableEventArgs(int messageSize) : base()
        {
            this.MessageSize = messageSize;
        }

        public int MessageSize { get; private set; }
    }

    public class StreamWatcher
    {
        public Stream stream { get; private set; }

        private byte[] sizeBuffer = new byte[2];

        public StreamWatcher(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException("stream");
            this.stream = stream;
            WatchNext();
        }

        protected void OnMessageAvailable(MessageAvailableEventArgs e)
        {
            var handler = MessageAvailable;
            if (handler != null)
                handler(this, e);
        }

        protected void WatchNext()
        {
            try
            {
                stream.BeginRead(sizeBuffer, 0, 2, new AsyncCallback(ReadCallback),
                    null);
            }
            catch (Exception) { }
        }

        private void ReadCallback(IAsyncResult ar)
        {
            try
            {
                int bytesRead = stream.EndRead(ar);
                if (bytesRead != 2)
                {
                    WatchNext();
                    return;
                }
                int messageSize = sizeBuffer[1] << 8 + sizeBuffer[0];
                OnMessageAvailable(new MessageAvailableEventArgs(messageSize));
                WatchNext();
            }
            catch (Exception) { }
        }

        public event MessageAvailableEventHandler MessageAvailable;
    }
}
