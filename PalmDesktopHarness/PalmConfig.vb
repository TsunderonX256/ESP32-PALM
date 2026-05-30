Namespace PalmDesktopHarness
    Friend Module PalmConfig
        ' Keep this VB profile switch in sync with the native CMake PALM_PROFILE
        ' setting. Mismatched VB/native profiles can load the wrong ROM or state file
        ' against a DLL built for a different hardware map.
#Const PALM_PROFILE_M100_EXPERIMENTAL = True
#Const PALM_PROFILE_IIIC_EXPERIMENTAL = False

        Public Enum HardwareProfile
            IIIx
            M100Experimental
            IIIcExperimental
        End Enum

#If PALM_PROFILE_IIIC_EXPERIMENTAL Then
        Public Const ActiveHardwareProfile As HardwareProfile = HardwareProfile.IIIcExperimental
        Public Const ProfileName As String = "Palm IIIc experimental"
        Public Const RomFileName As String = "Palm-IIIc-4.1-en.rom"
        Public Const StateFileName As String = "palm_iiic_state.bin"
        Public Const RamActivePreset As UInteger = RamPreset8M
        Public Const UseLcdContrastRegister As Boolean = True
        Public Const UseColorLcdPalette As Boolean = True
        Public Const AutoRestorePersistentState As Boolean = True
        Public Const PauseCpuWhileSleeping As Boolean = False
        Public Const NativeAutoRunCyclesPerSlice As Integer = 25000
#ElseIf PALM_PROFILE_M100_EXPERIMENTAL Then
        Public Const ActiveHardwareProfile As HardwareProfile = HardwareProfile.M100Experimental
        Public Const ProfileName As String = "Palm m100 experimental"
        Public Const RomFileName As String = "Palm-m100-3.51-en.rom"
        Public Const StateFileName As String = "palm_m100_state.bin"
        Public Const RamActivePreset As UInteger = RamPreset2M
        Public Const UseLcdContrastRegister As Boolean = True
        Public Const UseColorLcdPalette As Boolean = False
        Public Const AutoRestorePersistentState As Boolean = True
        Public Const PauseCpuWhileSleeping As Boolean = True
        Public Const NativeAutoRunCyclesPerSlice As Integer = 20000
#Else
        Public Const ActiveHardwareProfile As HardwareProfile = HardwareProfile.IIIx
        Public Const ProfileName As String = "Palm IIIx"
        Public Const RomFileName As String = "Palm-IIIx-3.1.rom"
        Public Const StateFileName As String = "palm_iiix_state.bin"
        Public Const RamActivePreset As UInteger = RamPreset4M
        Public Const UseLcdContrastRegister As Boolean = False
        Public Const UseColorLcdPalette As Boolean = False
        Public Const AutoRestorePersistentState As Boolean = True
        Public Const PauseCpuWhileSleeping As Boolean = True
        Public Const NativeAutoRunCyclesPerSlice As Integer = 20000
#End If

        Public Const RamBase As UInteger = &H0UI
        Public Const RamAllocTargetSize As UInteger = 256UI * 1024UI
        Public Const RamPreset256K As UInteger = 256UI * 1024UI
        Public Const RamPreset2M As UInteger = 2UI * 1024UI * 1024UI
        Public Const RamPreset4M As UInteger = 4UI * 1024UI * 1024UI
        Public Const RamPreset8M As UInteger = 8UI * 1024UI * 1024UI
        Public Const RamLogicalSize As UInteger = RamActivePreset
        Public Const RamTopAliasEnd As UInteger = &H1000000UI

        Public Const LcdWidth As Integer = 160
        Public Const LcdHeight As Integer = 160
        Public Const DigitizerWidth As Integer = 160
        Public Const SilkscreenHeight As Integer = 60
        Public Const DigitizerHeight As Integer = LcdHeight + SilkscreenHeight

        Public Const RomBase As UInteger = &H10C08000UI
        Public Const RomLowAliasBase As UInteger = &H10C00000UI
        Public Const RomLow24BitAliasBase As UInteger = RomLowAliasBase And &HFFFFFFUI
        Public Const Rom24BitBase As UInteger = RomBase And &HFFFFFFUI
        Public Const RomChipSelectSize As UInteger = 16UI * 1024UI * 1024UI
        Public Const Enable24BitAliases As Boolean = True

        Public Const DbRegBase As UInteger = &HFFFFF000UI
        Public Const DbReg24BitBase As UInteger = &HFFF000UI
        Public Const DbRegSize As UInteger = &H1000UI

        Public Const DefaultLcdContrastRegister As UShort = &HFFUS
        Public Const NormalLcdBackgroundR As Integer = 226
        Public Const NormalLcdBackgroundG As Integer = 230
        Public Const NormalLcdBackgroundB As Integer = 218
        Public Const NormalSilkscreenFillR As Integer = 150
        Public Const NormalSilkscreenFillG As Integer = 160
        Public Const NormalSilkscreenFillB As Integer = 130
        Public Const SleepPageBackgroundR As Integer = 224
        Public Const SleepPageBackgroundG As Integer = 228
        Public Const SleepPageBackgroundB As Integer = 214
        Public Const SleepLcdBackgroundR As Integer = 204
        Public Const SleepLcdBackgroundG As Integer = 211
        Public Const SleepLcdBackgroundB As Integer = 194
        Public Const SleepSilkscreenFillR As Integer = 134
        Public Const SleepSilkscreenFillG As Integer = 142
        Public Const SleepSilkscreenFillB As Integer = 116
        Public Const SleepLineR As Integer = 90
        Public Const SleepLineG As Integer = 95
        Public Const SleepLineB As Integer = 80
        Public Const BacklightPageBackgroundR As Integer = 18
        Public Const BacklightPageBackgroundG As Integer = 38
        Public Const BacklightPageBackgroundB As Integer = 24
        Public Const BacklightLcdBackgroundR As Integer = 10
        Public Const BacklightLcdBackgroundG As Integer = 28
        Public Const BacklightLcdBackgroundB As Integer = 14
        Public Const BacklightSleepLcdBackgroundR As Integer = 10
        Public Const BacklightSleepLcdBackgroundG As Integer = 18
        Public Const BacklightSleepLcdBackgroundB As Integer = 12
        Public Const BacklightPixelLowR As Integer = 14
        Public Const BacklightPixelLowG As Integer = 42
        Public Const BacklightPixelLowB As Integer = 18
        Public Const BacklightPixelHighR As Integer = 172
        Public Const BacklightPixelHighG As Integer = 255
        Public Const BacklightPixelHighB As Integer = 146
        Public Const BacklightSilkscreenFillR As Integer = 118
        Public Const BacklightSilkscreenFillG As Integer = 152
        Public Const BacklightSilkscreenFillB As Integer = 102
        Public Const BacklightLineR As Integer = 3
        Public Const BacklightLineG As Integer = 14
        Public Const BacklightLineB As Integer = 5

        Public Const NativeAutoRunSlicesPerTick As Integer = 20
        Public Const DefaultLcdRedrawIntervalMs As Integer = 100
    End Module
End Namespace
