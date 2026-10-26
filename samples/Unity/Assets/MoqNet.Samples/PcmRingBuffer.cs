using System;

namespace MoqNet.Samples.Unity
{
    /// <summary>Fixed-size float ring used to move PCM onto the audio thread.</summary>
    public sealed class PcmRingBuffer
    {
        private readonly float[] _buffer;
        private readonly object _gate = new object();
        private int _write;
        private int _read;
        private int _count;

        public PcmRingBuffer(int capacity)
        {
            _buffer = new float[capacity];
        }

        public void Clear()
        {
            lock (_gate)
            {
                _write = 0;
                _read = 0;
                _count = 0;
            }
        }

        public void Write(float[] samples, int offset, int length)
        {
            lock (_gate)
            {
                for (int i = 0; i < length; i++)
                {
                    if (_count == _buffer.Length)
                    {
                        _read = (_read + 1) % _buffer.Length;
                        _count--;
                    }

                    _buffer[_write] = samples[offset + i];
                    _write = (_write + 1) % _buffer.Length;
                    _count++;
                }
            }
        }

        public int Read(float[] destination, int offset, int length)
        {
            lock (_gate)
            {
                int copied = Math.Min(length, _count);
                for (int i = 0; i < copied; i++)
                {
                    destination[offset + i] = _buffer[_read];
                    _read = (_read + 1) % _buffer.Length;
                }

                _count -= copied;
                return copied;
            }
        }
    }
}
