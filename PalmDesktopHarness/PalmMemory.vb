Imports System.IO

Namespace PalmDesktopHarness
    Friend Enum PalmAccessKind
        Ram
        RamMirror
        Rom
        Register
        BusError
        Unmapped
    End Enum

    Friend Structure PalmAccessResult
        Public Sub New(kind As PalmAccessKind, address As UInteger, value As Byte, note As String)
            Me.Kind = kind
            Me.Address = address
            Me.Value = value
            Me.Note = note
        End Sub

        Public ReadOnly Kind As PalmAccessKind
        Public ReadOnly Address As UInteger
        Public ReadOnly Value As Byte
        Public ReadOnly Note As String
    End Structure

    Friend Structure PalmLcdDebug
        Public Property StartAddress As UInteger
        Public Property Width As UShort
        Public Property Height As UShort
        Public Property BytesPerLine As UShort
        Public Property PanelControl As Byte
        Public Property BitsPerPixel As Integer
        Public Property PanningOffset As Byte

        Public ReadOnly Property IsValid As Boolean
            Get
                Return StartAddress <> 0UI AndAlso Width > 0US AndAlso Width <= 320US AndAlso
                    Height > 0US AndAlso Height <= 320US AndAlso BytesPerLine > 0US
            End Get
        End Property
    End Structure

    Friend NotInheritable Class PalmMemory
        Private ReadOnly rom As Byte()
        Private ReadOnly ram As Byte()
        Private ReadOnly regs(CInt(PalmConfig.DbRegSize) - 1) As Byte

        Public Sub New(romPath As String, realRamSize As Integer)
            rom = File.ReadAllBytes(romPath)
            ram = New Byte(realRamSize - 1) {}
            InitDragonBallEzDefaults()
        End Sub

        Public ReadOnly Property RomSize As Integer
            Get
                Return rom.Length
            End Get
        End Property

        Public ReadOnly Property RealRamSize As Integer
            Get
                Return ram.Length
            End Get
        End Property

        Public Function Read8(address As UInteger, Optional instructionFetch As Boolean = False) As PalmAccessResult
            Dim offset As UInteger

            If TryRomOffset(address, offset) Then
                Return New PalmAccessResult(PalmAccessKind.Rom, address, rom(CInt(offset)), "ROM")
            End If

            If TryRegOffset(address, offset) Then
                Return New PalmAccessResult(PalmAccessKind.Register, address, regs(CInt(offset)), "DragonBall EZ register")
            End If

            If TryRamOffset(address, offset) Then
                Return New PalmAccessResult(PalmAccessKind.Ram, address, ram(CInt(offset)), "real RAM")
            End If

            If address >= PalmConfig.RamBase + PalmConfig.RamLogicalSize AndAlso
                address < PalmConfig.RamBase + PalmConfig.RamLogicalSize + 4UI Then
                Return New PalmAccessResult(PalmAccessKind.RamMirror, address, &H0, "RAM-size probe")
            End If

            If TryLogicalRamOffset(address, offset) Then
                If instructionFetch Then
                    Return New PalmAccessResult(PalmAccessKind.BusError, address, &HFF, "instruction fetch outside real RAM")
                End If

                Dim mirrored = CInt(offset Mod CUInt(ram.Length))
                Return New PalmAccessResult(PalmAccessKind.RamMirror, address, ram(mirrored), $"mirrored RAM -> ${mirrored:X6}")
            End If

            If address < PalmConfig.RamTopAliasEnd Then
                Return New PalmAccessResult(PalmAccessKind.BusError, address, &HFF, "RAM bank boundary")
            End If

            Return New PalmAccessResult(PalmAccessKind.Unmapped, address, &HFF, "unmapped")
        End Function

        Public Sub Write8(address As UInteger, value As Byte)
            Dim offset As UInteger

            If TryRegOffset(address, offset) Then
                regs(CInt(offset)) = value
                Return
            End If

            If TryRomOffset(address, offset) Then
                Return
            End If

            If TryRamOffset(address, offset) Then
                ram(CInt(offset)) = value
                Return
            End If

            If address >= PalmConfig.RamBase + PalmConfig.RamLogicalSize AndAlso
                address < PalmConfig.RamBase + PalmConfig.RamLogicalSize + 4UI Then
                Return
            End If

            If TryLogicalRamOffset(address, offset) Then
                ram(CInt(offset Mod CUInt(ram.Length))) = value
            End If
        End Sub

        Public Function Read16(address As UInteger, Optional instructionFetch As Boolean = False) As UShort
            Dim hi = Read8(address, instructionFetch).Value
            Dim lo = Read8(address + 1UI, instructionFetch).Value
            Return CUShort((CUShort(hi) << 8) Or lo)
        End Function

        Public Function Read32(address As UInteger) As UInteger
            Return (CUInt(Read16(address)) << 16) Or CUInt(Read16(address + 2UI))
        End Function

        Public Function ReadInstruction16(address As UInteger) As PalmAccessResult
            Return Read8(address, True)
        End Function

        Public Function LcdDebug() As PalmLcdDebug
            Dim panel = regs(&HA20)
            Return New PalmLcdDebug With {
                .StartAddress = GetReg32(&HA00US) And &H1FFFFFFEUI,
                .Width = GetReg16(&HA08US),
                .Height = CUShort(GetReg16(&HA0AUS) + 1US),
                .BytesPerLine = CUShort(regs(&HA05) * 2),
                .PanelControl = panel,
                .BitsPerPixel = 1 << (panel And &H3),
                .PanningOffset = regs(&HA2D)
            }
        End Function

        Public Function Inspect(address As UInteger, Optional instructionFetch As Boolean = False) As String
            Dim access = Read8(address, instructionFetch)
            Return $"${access.Address:X8}  {access.Kind,-9}  ${access.Value:X2}  {access.Note}"
        End Function

        Private Function TryRamOffset(address As UInteger, ByRef offset As UInteger) As Boolean
            If address >= PalmConfig.RamBase AndAlso address < PalmConfig.RamBase + CUInt(ram.Length) Then
                offset = address - PalmConfig.RamBase
                Return True
            End If

            Dim aliasBase = PalmConfig.RamTopAliasEnd - CUInt(ram.Length)
            If address >= aliasBase AndAlso address < PalmConfig.RamTopAliasEnd Then
                offset = address - aliasBase
                Return True
            End If

            Return False
        End Function

        Private Function TryLogicalRamOffset(address As UInteger, ByRef offset As UInteger) As Boolean
            If address >= PalmConfig.RamBase AndAlso address < PalmConfig.RamBase + PalmConfig.RamLogicalSize Then
                offset = address - PalmConfig.RamBase
                Return True
            End If

            Dim aliasBase = PalmConfig.RamTopAliasEnd - PalmConfig.RamLogicalSize
            If address >= aliasBase AndAlso address < PalmConfig.RamTopAliasEnd Then
                offset = address - aliasBase
                Return True
            End If

            Return False
        End Function

        Private Function TryRomOffset(address As UInteger, ByRef offset As UInteger) As Boolean
            If address >= PalmConfig.RomBase AndAlso address < PalmConfig.RomBase + CUInt(rom.Length) Then
                offset = address - PalmConfig.RomBase
                Return True
            End If

            If address >= PalmConfig.RomLowAliasBase AndAlso
                address < PalmConfig.RomBase AndAlso
                address < PalmConfig.RomLowAliasBase + CUInt(rom.Length) Then
                offset = address - PalmConfig.RomLowAliasBase
                Return True
            End If

            If address >= PalmConfig.RomLowAliasBase AndAlso
                address < PalmConfig.RomLowAliasBase + PalmConfig.RomChipSelectSize Then
                offset = (address - PalmConfig.RomLowAliasBase) Mod CUInt(rom.Length)
                Return True
            End If

            If PalmConfig.Enable24BitAliases AndAlso
                address >= PalmConfig.RomLow24BitAliasBase AndAlso
                address < PalmConfig.RomLow24BitAliasBase + PalmConfig.RomChipSelectSize Then
                offset = (address - PalmConfig.RomLow24BitAliasBase) Mod CUInt(rom.Length)
                Return True
            End If

            If PalmConfig.Enable24BitAliases AndAlso
                address >= PalmConfig.RomLow24BitAliasBase AndAlso
                address < PalmConfig.Rom24BitBase AndAlso
                address < PalmConfig.RomLow24BitAliasBase + CUInt(rom.Length) Then
                offset = address - PalmConfig.RomLow24BitAliasBase
                Return True
            End If

            If PalmConfig.Enable24BitAliases AndAlso
                address >= PalmConfig.Rom24BitBase AndAlso
                address < PalmConfig.Rom24BitBase + CUInt(rom.Length) Then
                offset = address - PalmConfig.Rom24BitBase
                Return True
            End If

            Return False
        End Function

        Private Function TryRegOffset(address As UInteger, ByRef offset As UInteger) As Boolean
            If address >= PalmConfig.DbRegBase Then
                offset = address - PalmConfig.DbRegBase
                Return offset < PalmConfig.DbRegSize
            End If

            If PalmConfig.Enable24BitAliases AndAlso
                address >= PalmConfig.DbReg24BitBase AndAlso
                address < PalmConfig.DbReg24BitBase + PalmConfig.DbRegSize Then
                offset = address - PalmConfig.DbReg24BitBase
                Return True
            End If

            Return False
        End Function

        Private Sub InitDragonBallEzDefaults()
            Put16(&H100US, &H0US)
            Put16(&H110US, &HE0US)
            Put16(&H116US, &H1800US)
            Put16(&H200US, &H2430US)
            Put16(&H202US, &H123US)
            Put16(&H304US, &HFFUS)
            Put16(&H306US, &HFFFFUS)
            regs(&H4) = &H10
            regs(&H5) = &H5
            regs(&HA05) = &HFF
            Put16(&HA08US, &H3FFUS)
            Put16(&HA0AUS, &H1FFUS)
            regs(&HA27) = &H40
            regs(&HA29) = &HFF
            regs(&HA31) = &HB9
            regs(&HA33) = &H84
        End Sub

        Private Sub Put16(offset As UShort, value As UShort)
            regs(offset) = CByte((value >> 8) And &HFF)
            regs(offset + 1) = CByte(value And &HFF)
        End Sub

        Private Function GetReg16(offset As UShort) As UShort
            Return CUShort((CUShort(regs(offset)) << 8) Or regs(offset + 1))
        End Function

        Private Function GetReg32(offset As UShort) As UInteger
            Return (CUInt(GetReg16(offset)) << 16) Or CUInt(GetReg16(CUShort(offset + 2US)))
        End Function
    End Class
End Namespace
