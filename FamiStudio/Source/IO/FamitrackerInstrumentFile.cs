using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace FamiStudio
{
    class FamitrackerInstrumentFile
    {
        // FTI instruments files
        static readonly string INST_HEADER = "FTI";
        static readonly string INST_VERSION = "2.4";
        public static readonly int MAX_SEQUENCE_ITEMS = /*128*/ 253;
        public static readonly int MAX_SEQUENCES = 128;
        public static readonly int OCTAVE_RANGE = 8;
        enum Inst_Type_t
        {
            INST_NONE = 0,
            INST_2A03 = 1,
            INST_VRC6,
            INST_VRC7,
            INST_FDS,
            INST_N163,
            INST_S5B
        };
        public enum Sequence_t
        {
            SEQ_VOLUME,
            SEQ_ARPEGGIO,
            SEQ_PITCH,
            SEQ_HIPITCH,        // TODO: remove this eventually
            SEQ_DUTYCYCLE,

            SEQ_COUNT
        };
        public static Instrument Load(int uniqueId, string filename)
        {
            var bytes = System.IO.File.ReadAllBytes(filename);
            if (!CheckFormat(bytes))
            {
               
                return null;
            }
            var instType = GetInstrumentType(bytes,out var offsetIndex);
            if (instType == Inst_Type_t.INST_NONE)
            {
                instType = Inst_Type_t.INST_2A03;
            }
            switch (instType)
            {
                case Inst_Type_t.INST_NONE:
                    break;
                case Inst_Type_t.INST_2A03:
                    return ConvertInstrument2A03(uniqueId, bytes);
                case Inst_Type_t.INST_VRC6:
                    break;
                case Inst_Type_t.INST_VRC7:
                    break;
                case Inst_Type_t.INST_FDS:
                    break;
                case Inst_Type_t.INST_N163:
                    break;
                case Inst_Type_t.INST_S5B:
                    break;
            }
            return null;
        }
        private static bool CheckFormat( byte[] inputdata )
        {
            //outdata = new string[0];
            //if(inputdata.Length != 1)
            //{//invalid format
               
            //    return false;
            //}
            ////str length == 1
            var targetHeaderByte = Encoding.ASCII.GetBytes(INST_HEADER);
            var currentVersionByte = Encoding.ASCII.GetBytes(INST_VERSION);
            if (inputdata.Length < targetHeaderByte.Length + currentVersionByte.Length + 1)
            {
                return false;
            }
            var headerByte = new byte[targetHeaderByte.Length];
            var versionByte = new byte[currentVersionByte.Length];
            Array.Copy(inputdata, headerByte, headerByte.Length);
            Array.Copy(inputdata, headerByte.Length , versionByte, 0 , currentVersionByte.Length);
            //check header
            if (!CompareByteArray(headerByte,targetHeaderByte))
            {
                return false;
            }

            //check version
            float currentVersion = float.Parse(INST_VERSION);
            var fileVersionString = Encoding.ASCII.GetString(versionByte);
            float fileVersion = float.MaxValue;
            try
            {
                fileVersion = float.Parse(fileVersionString);
            }
            catch
            {//error handle

            }
            
            if (fileVersion > currentVersion)
            {//version not supported
                return false;
            }

            return true;

        }
        private static Inst_Type_t GetInstrumentType( byte[] inputdata,out int offset)
        {
            var targetHeaderByte = Encoding.ASCII.GetBytes(INST_HEADER);
            var currentVersionByte = Encoding.ASCII.GetBytes(INST_VERSION);
            var instTypeByte = inputdata[targetHeaderByte.Length + currentVersionByte.Length];
            offset = targetHeaderByte.Length + currentVersionByte.Length + 1;
            if (inputdata.Length < targetHeaderByte.Length + currentVersionByte.Length + 1)
            {
                return Inst_Type_t.INST_NONE;
            }
            return (Inst_Type_t)instTypeByte;
        }
        private static string GetInstrumentName(byte[] inputdata)
        {
            var targetHeaderByte = Encoding.ASCII.GetBytes(INST_HEADER);
            var currentVersionByte = Encoding.ASCII.GetBytes(INST_VERSION);
            if (inputdata.Length < targetHeaderByte.Length + currentVersionByte.Length + 1 + 4)
            {
                return "";
            }
            var nameLengthByte = new byte[4];
            Array.Copy(inputdata, targetHeaderByte.Length + currentVersionByte.Length + 1, nameLengthByte, 0, 4);
            var nameLength = BitConverter.ToInt32(nameLengthByte, 0);
            if (nameLength >= 256)
            {
                return "";
            }
            if (inputdata.Length < targetHeaderByte.Length + currentVersionByte.Length + 1 + 4 + nameLength)
            {
                return "";
            }
            var nameByte = new byte[nameLength];
            Array.Copy(inputdata, targetHeaderByte.Length + currentVersionByte.Length + 1 + 4, nameByte, 0, nameLength);
            return Encoding.ASCII.GetString(nameByte);

        }
        private static Instrument ConvertInstrument2A03(int uniqueId , byte[] inputdata)
        {
            var instrument = new Instrument(uniqueId, GetInstrumentName(inputdata));
            return instrument;
        }
        private static bool CompareByteArray(byte[] a1,byte[] a2)
        {
            if(a1.Length != a2.Length)
            {
                return false;
            }
            var length = a1.Length;
            for(int i = 0; i < length; i++)
            {
                if(a1[i] != a2[i])
                {
                    return false;
                }
            }
            return true;

        }
    }
    abstract class ConvertInstrument
    {
        protected int idx = 0;
        protected byte[] data;
        protected int[] m_iSeqEnable = new int[(int)FamitrackerInstrumentFile.Sequence_t.SEQ_COUNT];
        protected int[] m_iSeqIndex = new int[(int)FamitrackerInstrumentFile.Sequence_t.SEQ_COUNT];
        protected sbyte[,] m_cSamples = new char[FamitrackerInstrumentFile.OCTAVE_RANGE,12];   // Samples
        protected sbyte[,] m_cSamplePitch = new char[FamitrackerInstrumentFile.OCTAVE_RANGE, 12];// Play pitch/loop
        protected sbyte[,] m_cSampleLoopOffset = new char[FamitrackerInstrumentFile.OCTAVE_RANGE, 12];// Loop offset
        protected sbyte[,] m_cSampleDelta = new char[FamitrackerInstrumentFile.OCTAVE_RANGE, 12];// Delta setting

        public Instrument Convert(int uniqueId, byte[] data, int idx)
        {
            this.idx = idx;
            this.data = data;
            return Convert(uniqueId);
        }
        protected abstract Instrument Convert(int uniqueId, int iVersion);

        protected string GetName()
        {
            byte[] temp;
            if (!ReadByte(sizeof(int), out temp))
            {
                return "";
            }
            
            var nameLength = BitConverter.ToInt32(temp, 0);
            if (nameLength >= 256)
            {
                return "";
            }
            if (!ReadByte(nameLength, out temp))
            {
                return "";
            }
            return Encoding.ASCII.GetString(temp);

        }

        protected bool ReadByte(int length,out byte[] result)
        {
            result = new byte[length];
            var beforeIdx = idx;

            if (!AddIndex(length))
            {
                return false;
            }
            Array.Copy(data, beforeIdx, result, 0, length);
            return true;
        }
        bool AddIndex(int additional)
        {
            idx += additional;
            if (data.Length < idx)
            {
                return false;
            }
            return true;
        }
        protected int GetFreeSequence(int Type)
        {
            // Return a free sequence slot, or -1 otherwise
            for (int i = 0; i < FamitrackerInstrumentFile.MAX_SEQUENCES; ++i)
            {
                if (GetSequenceItemCount(i, Type) == 0)
                    return i;
            }
            return -1;
        }
    }
    class ConvertInstrument2A03 : ConvertInstrument
    {
        const int SEQUENCE_COUNT = 5;
        public ConvertInstrument2A03()
        {
	        for (int i = 0; i<SEQUENCE_COUNT; ++i) {
		        m_iSeqEnable[i] = 0;
		        m_iSeqIndex[i] = 0;
	        }

	        for (int i = 0; i< FamitrackerInstrumentFile.OCTAVE_RANGE; ++i) {
		        for (int j = 0; j< 12; ++j) {
		        	m_cSamples[i,j] = 0;
		        	m_cSamplePitch[i,j] = 0;
		        	m_cSampleLoopOffset[i,j] = 0;
		        	m_cSampleDelta[i,j] = -1;
		        }
	        }
        }
        protected override Instrument Convert(int uniqueId,int iVersion)
        {
            var name = GetName();
            var instrument = new Instrument(uniqueId, name);
            byte[] temp;
            var intSize = sizeof(int);
            var byteSize = 1;
            if (!ReadByte(byteSize, out temp))
                return null;
            
            byte seqCount = temp[0];
            for (int i = 0; i < seqCount; i++)
            {
                if (!ReadByte(byteSize, out temp))
                    return null;
                var enabled = temp[0];
                if (enabled == 1)
                {
                    if (!ReadByte(intSize, out temp))
                        return null;
                    
                    var count = BitConverter.ToInt32(temp, 0);
                    if(count < 0 || FamitrackerInstrumentFile.MAX_SEQUENCE_ITEMS < count)
                        return null;
                    
                }
                else
                {

                }
            }

            return instrument;
        }
    }
}
