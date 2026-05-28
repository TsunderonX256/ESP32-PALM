Namespace PalmDesktopHarness
    Friend Module PalmConfig
' Keep this VB profile switch in sync with the native CMake PALM_PROFILE
' setting. Mismatched VB/native profiles can load the wrong ROM or state file
' against a DLL built for a different hardware map.
#Const PALM_PROFILE_M100_EXPERIMENTAL = True

        Public Enum HardwareProfile
            IIIx
            M100Experimental
        End Enum

#If PALM_PROFILE_M100_EXPERIMENTAL Then
        Public Const ActiveHardwareProfile As HardwareProfile = HardwareProfile.M100Experimental
        Public Const ProfileName As String = "Palm m100 experimental"
        Public Const RomFileName As String = "Palm-m100-3.51-en.rom"
        Public Const StateFileName As String = "palm_m100_state.bin"
        Public Const RamActivePreset As UInteger = RamPreset2M
        Public Const LcdWidth As Integer = 160
        Public Const LcdHeight As Integer = 160
        Public Const DigitizerWidth As Integer = 160
        Public Const SilkscreenHeight As Integer = 60
#Else
        Public Const ActiveHardwareProfile As HardwareProfile = HardwareProfile.IIIx
        Public Const ProfileName As String = "Palm IIIx"
        Public Const RomFileName As String = "Palm-IIIx-3.1.rom"
        Public Const StateFileName As String = "palm_iiix_state.bin"
        Public Const RamActivePreset As UInteger = RamPreset4M
        Public Const LcdWidth As Integer = 160
        Public Const LcdHeight As Integer = 160
        Public Const DigitizerWidth As Integer = 160
        Public Const SilkscreenHeight As Integer = 60
#End If

        Public Const RamBase As UInteger = &H0UI
        Public Const RamAllocTargetSize As UInteger = 256UI * 1024UI
        Public Const RamPreset256K As UInteger = 256UI * 1024UI
        Public Const RamPreset2M As UInteger = 2UI * 1024UI * 1024UI
        Public Const RamPreset4M As UInteger = 4UI * 1024UI * 1024UI
        Public Const RamLogicalSize As UInteger = RamActivePreset
        Public Const RamTopAliasEnd As UInteger = &H1000000UI

        Public Const RomBase As UInteger = &H10C08000UI
        Public Const RomLowAliasBase As UInteger = &H10C00000UI
        Public Const RomLow24BitAliasBase As UInteger = RomLowAliasBase And &HFFFFFFUI
        Public Const Rom24BitBase As UInteger = RomBase And &HFFFFFFUI
        Public Const RomChipSelectSize As UInteger = 16UI * 1024UI * 1024UI
        Public Const Enable24BitAliases As Boolean = True

        Public Const DbRegBase As UInteger = &HFFFFF000UI
        Public Const DbReg24BitBase As UInteger = &HFFF000UI
        Public Const DbRegSize As UInteger = &H1000UI

        Public Const DigitizerHeight As Integer = LcdHeight + SilkscreenHeight
        Public Const DefaultLcdRedrawIntervalMs As Integer = 100
    End Module
End Namespace
