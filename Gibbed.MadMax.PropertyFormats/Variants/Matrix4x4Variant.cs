﻿/* Copyright (c) 2015 Rick (rick 'at' gibbed 'dot' us)
 * 
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 * 
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 * 
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 * 
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 * 
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.Globalization;
using System.IO;
using Gibbed.IO;
using Gibbed.MadMax.FileFormats;
using Gibbed.MadMax.PropertyFormats;

namespace Gibbed.MadMax.PropertyFormats.Variants
{
    // ReSharper disable InconsistentNaming
    public class Matrix4x4Variant : IVariant, RawPropertyContainerFile.IRawVariant, PropertyContainerFile.IRawVariant
    // ReSharper restore InconsistentNaming
    {
        private FileFormats.Matrix4x4 _Value;

        public FileFormats.Matrix4x4 Value
        {
            get { return this._Value; }
            set { this._Value = value; }
        }

        public string Tag
        {
            get { return "mat"; }
        }

        public void Parse(string text)
        {
            var parts = text.Split(',');
            if (parts.Length != 4 * 4)
            {
                throw new FormatException("mat requires 12 float values delimited by commas");
            }

            var m11 = float.Parse(parts[0], CultureInfo.InvariantCulture);
            var m12 = float.Parse(parts[1], CultureInfo.InvariantCulture);
            var m13 = float.Parse(parts[2], CultureInfo.InvariantCulture);
            var m14 = float.Parse(parts[3], CultureInfo.InvariantCulture);
            var m21 = float.Parse(parts[4], CultureInfo.InvariantCulture);
            var m22 = float.Parse(parts[5], CultureInfo.InvariantCulture);
            var m23 = float.Parse(parts[6], CultureInfo.InvariantCulture);
            var m24 = float.Parse(parts[7], CultureInfo.InvariantCulture);
            var m31 = float.Parse(parts[8], CultureInfo.InvariantCulture);
            var m32 = float.Parse(parts[9], CultureInfo.InvariantCulture);
            var m33 = float.Parse(parts[10], CultureInfo.InvariantCulture);
            var m34 = float.Parse(parts[11], CultureInfo.InvariantCulture);
            var m41 = float.Parse(parts[12], CultureInfo.InvariantCulture);
            var m42 = float.Parse(parts[13], CultureInfo.InvariantCulture);
            var m43 = float.Parse(parts[14], CultureInfo.InvariantCulture);
            var m44 = float.Parse(parts[15], CultureInfo.InvariantCulture);
            this._Value = new FileFormats.Matrix4x4(m11, m12, m13, m14, m21, m22, m23, m24, m31, m32, m33, m34, m41, m42, m43, m44);
        }

        public string Compose(ProjectData.HashList<uint> hashNames)
        {
            return String.Format(
                "{0},{1},{2},{3}, {4},{5},{6},{7}, {8},{9},{10},{11}, {12},{13},{14},{15}",
                this._Value.M11.ToString(CultureInfo.InvariantCulture),
                this._Value.M12.ToString(CultureInfo.InvariantCulture),
                this._Value.M13.ToString(CultureInfo.InvariantCulture),
                this._Value.M14.ToString(CultureInfo.InvariantCulture),
                this._Value.M21.ToString(CultureInfo.InvariantCulture),
                this._Value.M22.ToString(CultureInfo.InvariantCulture),
                this._Value.M23.ToString(CultureInfo.InvariantCulture),
                this._Value.M24.ToString(CultureInfo.InvariantCulture),
                this._Value.M31.ToString(CultureInfo.InvariantCulture),
                this._Value.M32.ToString(CultureInfo.InvariantCulture),
                this._Value.M33.ToString(CultureInfo.InvariantCulture),
                this._Value.M34.ToString(CultureInfo.InvariantCulture),
                this._Value.M41.ToString(CultureInfo.InvariantCulture),
                this._Value.M42.ToString(CultureInfo.InvariantCulture),
                this._Value.M43.ToString(CultureInfo.InvariantCulture),
                this._Value.M44.ToString(CultureInfo.InvariantCulture));
        }

        #region RawPropertyContainerFile
        RawPropertyContainerFile.VariantType RawPropertyContainerFile.IRawVariant.Type
        {
            get { return RawPropertyContainerFile.VariantType.Matrix4x4; }
        }

        void RawPropertyContainerFile.IRawVariant.Serialize(Stream output, Endian endian)
        {
            FileFormats.Matrix3x4.Write(output, Matrix3x4.Read(this._Value), endian);
        }

        /*((void (__fastcall *)(NLibrary::CStream *, CMatrix3x4f *, __int64))in_stream->ReadData)(
        in_stream,
        &mat_data,
        48i64);
      *(_QWORD *)new_matrix.e = *(_QWORD *)&mat_data.m[0][0];
      *(_QWORD *)&new_matrix.m02 = LODWORD(mat_data.m[0][2]);
      *(_QWORD *)&new_matrix.m10 = *(_QWORD *)&mat_data.m[0][3];
      *(_QWORD *)&new_matrix.m12 = LODWORD(mat_data.m[1][1]);
      *(_QWORD *)&new_matrix.m20 = *(_QWORD *)&mat_data.m[1][2];
      *(_QWORD *)&new_matrix.m22 = LODWORD(mat_data.m[2][0]);
      *(_QWORD *)&new_matrix.m30 = *(_QWORD *)&mat_data.m[2][1];
      *(_QWORD *)&new_matrix.m32 = __PAIR64__(LODWORD(FLOAT_1_0), LODWORD(mat_data.m[2][3]));
      TVariant<CPCBlockAllocator<unsigned int>>::operator=<CMatrix4f>(this, &new_matrix);
        */

        void RawPropertyContainerFile.IRawVariant.Deserialize(Stream input, Endian endian)
        {
            this._Value = Matrix4x4.Read(FileFormats.Matrix3x4.Read(input, endian));
        }
        #endregion

        #region PropertyContainerFile
        PropertyContainerFile.VariantType PropertyContainerFile.IRawVariant.Type
        {
            get { return PropertyContainerFile.VariantType.Matrix4x4; }
        }

        bool PropertyContainerFile.IRawVariant.IsSimple
        {
            get { return true; }
        }

        uint PropertyContainerFile.IRawVariant.Alignment
        {
            get { return 16; }
        }

        void PropertyContainerFile.IRawVariant.Serialize(Stream output, Endian endian)
        {
            FileFormats.Matrix4x4.Write(output, this._Value, endian);
        }

        void PropertyContainerFile.IRawVariant.Deserialize(Stream input, Endian endian)
        {
            this._Value = FileFormats.Matrix4x4.Read(input, endian);
        }
        #endregion
    }
}