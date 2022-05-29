using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Net.Sockets;
namespace TCPlinux;

public class CryptoLink
{
        public ICryptoTransform Encryptor { get; set; }

        public ICryptoTransform Decryptor { get; set; }


        public byte[] AddPadding(byte[] original, int blockSize)
        {
            byte[] bytes = BitConverter.GetBytes(original.Length);
            int num2 = original.Length + bytes.Length;
            if ((num2 % blockSize) != 0)
            {
                num2 = ((num2 / blockSize) + 1) * blockSize;
            }
            byte[] array = new byte[num2];
            bytes.CopyTo(array, 0);
            for (int i = 0; i < original.Length; i++)
            {
                array[i + bytes.Length] = original[i];
            }
            return array;
        }
        public string Decrypt(byte[] buffer, int length)
        {
            if (this.Decryptor == null)
            {
                return Encoding.UTF8.GetString(buffer, 0, length);
            }
            byte[] outputBuffer = new byte[this.Decryptor.OutputBlockSize * (1 + (buffer.Length / this.Decryptor.OutputBlockSize))];
            this.Decryptor.TransformBlock(buffer, 0, length, outputBuffer, 0);
            byte[] bytes = this.RemovePadding(outputBuffer, this.Decryptor.OutputBlockSize);
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
        }

        public byte[] Encrypt(string msg)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(msg);
            if (this.Encryptor != null)
            {
                byte[] inputBuffer = this.AddPadding(bytes, this.Encryptor.OutputBlockSize);
                byte[] outputBuffer = new byte[inputBuffer.Length];
                this.Encryptor.TransformBlock(inputBuffer, 0, inputBuffer.Length, outputBuffer, 0);
                bytes = outputBuffer;
            }
            return bytes;
        }
        public void EncryptAndSend(string msg, Socket s)
        {
            byte[] buffer = this.Encrypt(msg);
            s.Send(buffer, buffer.Length, SocketFlags.None);
        }
        public string ReceiveAndDecrypt(Socket s)
        {
            byte[] buffer = new byte[0x400];
            int length = s.Receive(buffer, buffer.Length, SocketFlags.None);
            if (length == 0)
            {
                throw new ApplicationException("No bytes received from the endpoint");
            }
            return this.Decrypt(buffer, length);
        }

        public byte[] RemovePadding(byte[] original, int blockSize)
        {
            if (original.Length < blockSize)
            {
                throw new ApplicationException("Can't remove padding: buffer size is smaller than a blockSize");
            }
            int num = BitConverter.ToInt32(original, 0);
            if (num > (original.Length - 4))
            {
                throw new ApplicationException("Can't remove padding: recorded length of data is greater than a buffer size");
            }
            byte[] buffer = new byte[num];
            for (int i = 0; i < num; i++)
            {
                buffer[i] = original[i + 4];
            }
            return buffer;
        }
 
}