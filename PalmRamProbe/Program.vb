Imports System.IO
Imports System.Runtime.InteropServices

Module Program
    <StructLayout(LayoutKind.Sequential)>
    Private Structure PalmNativeDebug
        Public RamReadCount As UInteger
        Public RamWriteCount As UInteger
        Public RomReadCount As UInteger
        Public RegReadCount As UInteger
        Public RegWriteCount As UInteger
        Public BusErrorCount As UInteger
        Public InstrBusErrorCount As UInteger
        Public LastReadAddress As UInteger
        Public LastWriteAddress As UInteger
        Public LastBusErrorAddress As UInteger
        Public LastRegReadOffset As UShort
        Public LastRegWriteOffset As UShort
        Public LastRegWriteValue As Byte
        Public LcdWriteCount As UInteger
        Public LastLcdWriteOffset As UShort
        Public LastLcdWriteValue As Byte
        Public InstructionCount As UInteger
        Public LastInstructionPc As UInteger
        Public LastOpcode As UShort
        Public AdsCommandCount As UInteger
        Public PenXRaw As UShort
        Public PenYRaw As UShort
        Public PenDown As UShort
        Public LastAdsCommand As UShort
        Public LastAdsChannel As UShort
        Public LastAdsConversion As UShort
        Public LastAdsResponse As UShort
        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=8)>
        Public LastAdsChannelConversion As UShort()
        <MarshalAs(UnmanagedType.ByValArray, SizeConst:=8)>
        Public AdsChannelCounts As UInteger()
    End Structure

    <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Function palm_native_init(rom As Byte(), romSize As UInteger, ramSize As UInteger) As Integer
    End Function

    <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Function palm_native_execute(cycles As Integer) As Integer
    End Function

    <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Function palm_native_is_asleep() As Integer
    End Function

    <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Sub palm_native_set_power_button(down As Integer)
    End Sub

    <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Sub palm_native_get_debug(ByRef debug As PalmNativeDebug)
    End Sub

    <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Function palm_native_get_pc() As UInteger
    End Function

    <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Function palm_native_lcd_start() As UInteger
    End Function

    <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Function palm_native_lcd_width() As UShort
    End Function

    <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Function palm_native_lcd_height() As UShort
    End Function

    <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Function palm_native_ram_dirty_pages() As UInteger
    End Function

    <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
    Private Function palm_native_ram_highest_written() As UInteger
    End Function

    Function Main(args As String()) As Integer
        If args.Length < 2 Then
            Console.Error.WriteLine("usage: PalmRamProbe <rom-path> <ram-bytes>")
            Return 2
        End If

        Dim romPath = Path.GetFullPath(args(0))
        Dim ramSize As UInteger
        If Not UInteger.TryParse(args(1), ramSize) Then
            Console.Error.WriteLine("invalid RAM byte count")
            Return 2
        End If

        Dim rom = File.ReadAllBytes(romPath)
        If palm_native_init(rom, CUInt(rom.Length), ramSize) = 0 Then
            Console.WriteLine($"{ramSize \ 1024UI,4}   init failed")
            Return 1
        End If

        Dim ranTotal As ULong = 0
        If palm_native_is_asleep() <> 0 Then
            palm_native_set_power_button(1)
            For i = 1 To 8
                ranTotal += CULng(Math.Max(0, palm_native_execute(10000)))
            Next
            palm_native_set_power_button(0)
        End If

        Dim lcdText = "no"
        For i = 1 To 600
            ranTotal += CULng(Math.Max(0, palm_native_execute(100000)))
            If i Mod 20 = 0 Then
                If palm_native_lcd_start() <> 0UI AndAlso palm_native_lcd_width() = 160US AndAlso palm_native_lcd_height() = 160US Then
                    lcdText = "160x160"
                End If
            End If
        Next

        Dim debug As New PalmNativeDebug()
        palm_native_get_debug(debug)
        Dim dirtyKb = palm_native_ram_dirty_pages() * 4UI
        Dim highWrite = palm_native_ram_highest_written()
        Console.WriteLine($"{ramSize \ 1024UI,4}   {lcdText,-7} ${palm_native_get_pc():X8} {ranTotal,8} {dirtyKb,7} ${highWrite:X6} {debug.BusErrorCount}/{debug.InstrBusErrorCount}")
        Return 0
    End Function
End Module
