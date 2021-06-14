// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.
using System;
using UnityEngine;
using System.Runtime.InteropServices;

namespace Microsoft.Acoustics
{
    public class AcousticsPALPublic
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct TritonAcousticParameters
        {
            public static readonly float FailureCode = -1e10f;
            public float DirectDelay;
            public float DirectLoudnessDB;
            public float DirectAzimuth;
            public float DirectElevation;

            public float ReflectionsDelay;
            public float ReflectionsLoudnessDB;

            public float ReflLoudnessDB_Channel_0;
            public float ReflLoudnessDB_Channel_1;
            public float ReflLoudnessDB_Channel_2;
            public float ReflLoudnessDB_Channel_3;
            public float ReflLoudnessDB_Channel_4;
            public float ReflLoudnessDB_Channel_5;

            public float EarlyDecayTime;
            public float ReverbTime;

            public void CopyValuesFrom(TritonAcousticParameters other)
            {
                DirectDelay = other.DirectDelay;
                DirectLoudnessDB = other.DirectLoudnessDB;
                DirectAzimuth = other.DirectAzimuth;
                DirectElevation = other.DirectElevation;
                ReflectionsDelay = other.ReflectionsDelay;
                ReflectionsLoudnessDB = other.ReflectionsLoudnessDB;
                ReflLoudnessDB_Channel_0 = other.ReflLoudnessDB_Channel_0;
                ReflLoudnessDB_Channel_1 = other.ReflLoudnessDB_Channel_1;
                ReflLoudnessDB_Channel_2 = other.ReflLoudnessDB_Channel_2;
                ReflLoudnessDB_Channel_3 = other.ReflLoudnessDB_Channel_3;
                ReflLoudnessDB_Channel_4 = other.ReflLoudnessDB_Channel_4;
                ReflLoudnessDB_Channel_5 = other.ReflLoudnessDB_Channel_5;
                EarlyDecayTime = other.EarlyDecayTime;
                ReverbTime = other.ReverbTime;
            }

            public void ResetToError()
            {
                DirectDelay = FailureCode;
                DirectLoudnessDB = FailureCode;
                DirectAzimuth = FailureCode;
                DirectElevation = FailureCode;

                ReflectionsDelay = FailureCode;
                ReflectionsLoudnessDB = FailureCode;

                ReflLoudnessDB_Channel_0 = FailureCode;
                ReflLoudnessDB_Channel_1 = FailureCode;
                ReflLoudnessDB_Channel_2 = FailureCode;
                ReflLoudnessDB_Channel_3 = FailureCode;
                ReflLoudnessDB_Channel_4 = FailureCode;
                ReflLoudnessDB_Channel_5 = FailureCode;

                EarlyDecayTime = FailureCode;
                ReverbTime = FailureCode;
        }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TritonVec3f
        {
            public float x;
            public float y;
            public float z;

            public TritonVec3f(float a, float b, float c) { x = a; y = b; z = c; }
            public TritonVec3f(Vector3 vec) { x = vec.x; y = vec.y; z = vec.z; }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TritonVec3i
        {
            public int x;
            public int y;
            public int z;

            public TritonVec3i(int a, int b, int c) { x = a; y = b; z = c; }
        }
    }

    internal class AcousticsPAL
    {

        [StructLayout(LayoutKind.Sequential)]
        public struct ATKMatrix4x4
        {
            public float m11;
            public float m12;
            public float m13;
            public float m14;
            public float m21;
            public float m22;
            public float m23;
            public float m24;
            public float m31;
            public float m32;
            public float m33;
            public float m34;
            public float m41;
            public float m42;
            public float m43;
            public float m44;

            public ATKMatrix4x4(Matrix4x4 a)
            {
                m11 = a.m00;
                m12 = a.m01;
                m13 = a.m02;
                m14 = a.m03;
                m21 = a.m10;
                m22 = a.m11;
                m23 = a.m12;
                m24 = a.m13;
                m31 = a.m20;
                m32 = a.m21;
                m33 = a.m22;
                m34 = a.m23;
                m41 = a.m30;
                m42 = a.m31;
                m43 = a.m32;
                m44 = a.m33;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TritonAcousticParametersDebug
        {
            public int SourceId;
            public AcousticsPALPublic.TritonVec3f SourcePosition;
            public AcousticsPALPublic.TritonVec3f ListenerPosition;
            public float Outdoorness;
            public AcousticsPALPublic.TritonAcousticParameters AcousticParameters;
        }

        public enum ProbeLoadState
        {
            Loaded,
            NotLoaded,
            LoadFailed,
            LoadInProgress,
            DoesNotExist,
            Invalid
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ProbeMetadata
        {
            // Current loading state of this probe
            public ProbeLoadState State;

            // World location of this probe
            public AcousticsPALPublic.TritonVec3f Location;

            // Corners of the cubical region around this probe
            // for which it has data
            public AcousticsPALPublic.TritonVec3f DataMinCorner;
            public AcousticsPALPublic.TritonVec3f DataMaxCorner;
        }

        // Structs for Wwise integration
        [StructLayout(LayoutKind.Sequential)]
        public struct UserDesign
        {
            public float OcclusionMultiplier;
            public float WetnessAdjustment;
            public float DecayTimeMultiplier;
            public float OutdoornessAdjustment;
            public float TransmissionDb;
            public float DRRDistanceWarp;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct TritonWwiseParams
        {
            public UInt64 ObjectId;
            public AcousticsPALPublic.TritonAcousticParameters TritonParams;
            public float Outdoorness;
            public UserDesign Design;
        }

        // Import the functions from the DLL
        // Define the dll name based on the target platform
#if UNITY_STANDALONE_WIN || UNITY_WSA || UNITY_ANDROID || UNITY_XBOXONE
    const string TritonDll = "Triton";
    const string SpatializerDll = "AudioPluginMicrosoftAcoustics";
    const string WwiseDll = "AcousticsWwiseDll";
#elif UNITY_STANDALONE_OSX
    // Triton dylib is included inside AudioPluginMicrosoftAcoustics bundle file
    // (virtual directory) on MacOS, specify bundle name in order to bind to 
    // libTriton.dylib exports
    const string TritonDll = "AudioPluginMicrosoftAcoustics";
    const string SpatializerDll = "AudioPluginMicrosoftAcoustics";
#else
        // No other platforms are currently supported
        const string TritonDll = " ";
        const string SpatializerDll = " ";
#endif

// MacOS will error out if Wwise isn't installed, but doesn't complain on other platforms
#if UNITY_STANDALONE_WIN || UNITY_WSA || UNITY_ANDROID || UNITY_XBOXONE
        [DllImport(WwiseDll)]
        public static extern bool AcousticsWwise_SetTritonData(IntPtr paramsArray, int count);
        [DllImport(WwiseDll)]
        public static extern bool AcousticsWwise_SetEngineUnitsPerMeter(uint units);
#endif

        // Spatializer Exports
        [DllImport(SpatializerDll)]
        public static extern bool Spatializer_SetTritonHandle(IntPtr triton);

        [DllImport(SpatializerDll)]
        public static extern void Spatializer_SetAceFileLoaded(bool loaded);

        [DllImport(SpatializerDll)]
        public static extern void Spatializer_SetTransforms(ATKMatrix4x4 worldToLocal, ATKMatrix4x4 localToWorld);

        [DllImport(SpatializerDll)]
        public static extern bool Spatializer_GetDebugInfo(out IntPtr debugInfo, out int count);

        [DllImport(SpatializerDll)]
        public static extern void Spatializer_FreeDebugInfo(IntPtr debugInfo);

        // Triton API Exports
        [DllImport(TritonDll)]
        public static extern bool Triton_CreateInstance(bool debug, out IntPtr triton);

        [DllImport(TritonDll)]
        public static extern bool Triton_LoadAceFile(IntPtr triton, [MarshalAs(UnmanagedType.LPStr)] string filename);

        [DllImport(TritonDll)]
        public static extern bool Triton_LoadAll(IntPtr triton, bool block);

        [DllImport(TritonDll)]
        public static extern bool Triton_UnloadAll(IntPtr triton, bool block);

        [DllImport(TritonDll)]
        public static extern bool Triton_LoadRegion(IntPtr triton, AcousticsPALPublic.TritonVec3f center, AcousticsPALPublic.TritonVec3f length, bool unloadOutside, bool block, out int probesLoaded);

        [DllImport(TritonDll)]
        public static extern bool Triton_UnloadRegion(IntPtr triton, AcousticsPALPublic.TritonVec3f center, AcousticsPALPublic.TritonVec3f length, bool block);

        [DllImport(TritonDll)]
        public static extern bool Triton_QueryAcoustics(IntPtr triton, AcousticsPALPublic.TritonVec3f sourcePosition, AcousticsPALPublic.TritonVec3f listenerPosition, out AcousticsPALPublic.TritonAcousticParameters acousticParams);

        [DllImport(TritonDll)]
        public static extern bool Triton_GetOutdoornessAtListener(IntPtr triton, AcousticsPALPublic.TritonVec3f listenerPosition, out float outdoorness);

        [DllImport(TritonDll)]
        public static extern bool Triton_DestroyInstance(IntPtr triton);

        [DllImport(TritonDll)]
        public static extern bool Triton_Clear(IntPtr triton);

        [DllImport(TritonDll)]
        public static extern bool Triton_GetProbeCount(IntPtr triton, out int count);

        [DllImport(TritonDll)]
        public static extern bool Triton_GetProbeMetadata(IntPtr triton, int index, out ProbeMetadata metadata);

        [DllImport(TritonDll)]
        public static extern bool Triton_GetVoxelMapSection(
            IntPtr triton,
            AcousticsPALPublic.TritonVec3f minCorner,
            AcousticsPALPublic.TritonVec3f maxCorner,
            out IntPtr section);

        [DllImport(TritonDll)]
        public static extern bool Triton_GetPreappliedTransform(
            IntPtr triton,
            out ATKMatrix4x4 preappliedTransform);

        [DllImport(TritonDll)]
        public static extern bool VoxelMapSection_Destroy(IntPtr section);

        [DllImport(TritonDll)]
        public static extern bool VoxelMapSection_GetCellCount(IntPtr section, out AcousticsPALPublic.TritonVec3i count);

        [DllImport(TritonDll)]
        public static extern bool VoxelMapSection_IsVoxelWall(IntPtr section, AcousticsPALPublic.TritonVec3i cell);

        [DllImport(TritonDll)]
        public static extern bool VoxelMapSection_GetMinCorner(IntPtr section, out AcousticsPALPublic.TritonVec3f value);

        [DllImport(TritonDll)]
        public static extern bool VoxelMapSection_GetCellIncrementVector(IntPtr section, out AcousticsPALPublic.TritonVec3f vector);
        //#endif

    }
}