Imports System.Runtime.InteropServices
Imports System.Text

Namespace PalmDesktopHarness
    <StructLayout(LayoutKind.Sequential)>
    Friend Structure PalmNativeDebug
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

    Friend NotInheritable Class NativeMusashi
        Private Sub New()
        End Sub

        ' Core lifecycle and execution
        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_init(rom As Byte(), romSize As UInteger, ramSize As UInteger) As Integer
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Sub palm_native_warm_reset()
        End Sub

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_execute(cycles As Integer) As Integer
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_advance_time_ms(elapsedMs As UInteger) As Integer
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_service_wake(cycles As Integer) As Integer
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Sub palm_native_set_trace_enabled(enabled As Integer)
        End Sub

        ' State persistence
        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_state_size() As UInteger
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_save_state(<Out> buffer As Byte(), bufferSize As UInteger) As Integer
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_load_state(buffer As Byte(), bufferSize As UInteger) As Integer
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_install_prc_image(buffer As Byte(), bufferSize As UInteger, ByRef result As UInteger) As Integer
        End Function

        ' Serial/HotSync transport
        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_uart_write_rx(buffer As Byte(), count As UInteger) As UInteger
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_uart_read_tx(<Out> buffer As Byte(), count As UInteger) As UInteger
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_uart_rx_count() As UInteger
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_uart_tx_count() As UInteger
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_uart_rx_overrun_count() As UInteger
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_uart_tx_overrun_count() As UInteger
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_uart_misc() As UShort
        End Function

        ' PWM buzzer state
        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_sound_enabled() As Integer
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_sound_frequency() As Double
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_sound_duty() As Double
        End Function

        ' CPU/debug inspection
        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_get_pc() As UInteger
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_get_sp() As UInteger
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_get_d0() As UInteger
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_get_a0() As UInteger
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_get_d(index As Integer) As UInteger
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_get_a(index As Integer) As UInteger
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Sub palm_native_get_debug(ByRef debug As PalmNativeDebug)
        End Sub

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_ads_channel_conversion(channel As Integer) As UShort
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_ads_channel_count(channel As Integer) As UInteger
        End Function

        ' LCD/framebuffer access
        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_lcd_start() As UInteger
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_lcd_width() As UShort
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_lcd_height() As UShort
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_lcd_pitch() As UShort
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_lcd_panel() As Byte
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_lcd_pan() As Byte
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_lcd_contrast() As UShort
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_lcd_dirty() As Integer
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_lcd_frame_ready() As Integer
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Sub palm_native_lcd_mark_clean()
        End Sub

        ' Device metadata and input
        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_get_build_id() As UInteger
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_is_asleep() As Integer
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Sub palm_native_set_power_button(down As Integer)
        End Sub

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Sub palm_native_set_hotsync_button(down As Integer)
        End Sub

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Sub palm_native_set_button_bits(bits As UShort, down As Integer)
        End Sub

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_probe32(address As UInteger) As UInteger
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_peek8(address As UInteger) As Byte
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_copy_memory(address As UInteger, <Out> buffer As Byte(), count As UInteger) As UInteger
        End Function

        ' RAM diagnostics
        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_ram_physical_size() As UInteger
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_ram_dirty_pages() As UInteger
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Function palm_native_ram_highest_written() As UInteger
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Sub palm_native_set_pen(down As Integer, x As UShort, y As UShort)
        End Sub

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl)>
        Friend Shared Sub palm_native_set_pen_raw(down As Integer, rawX As UShort, rawY As UShort)
        End Sub

        ' Bring-up trace helpers
        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
        Friend Shared Function palm_native_copy_trace(buffer As StringBuilder, bufferSize As UInteger) As UInteger
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
        Friend Shared Function palm_native_copy_pc_trace(buffer As StringBuilder, bufferSize As UInteger) As UInteger
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
        Friend Shared Function palm_native_copy_first_pc_trace(buffer As StringBuilder, bufferSize As UInteger) As UInteger
        End Function

        <DllImport("PalmMusashi.dll", CallingConvention:=CallingConvention.Cdecl, CharSet:=CharSet.Ansi)>
        Friend Shared Function palm_native_copy_jump_trace(buffer As StringBuilder, bufferSize As UInteger) As UInteger
        End Function
    End Class
End Namespace
