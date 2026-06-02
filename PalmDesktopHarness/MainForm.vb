Imports System.Drawing
Imports System.Collections.Generic
Imports System.Globalization
Imports System.IO
Imports System.Linq
Imports System.Media
Imports System.Text
Imports System.Windows.Forms

Namespace PalmDesktopHarness
    Friend NotInheritable Class MainForm
        Inherits Form

        Private ReadOnly memory As PalmMemory
        Private ReadOnly lcdPanel As LcdPanel
        Private ReadOnly hardwarePanel As HardwareButtonPanel
        Private ReadOnly devicePanel As FlowLayoutPanel
        Private ReadOnly autoRunTimer As Timer
        Private ReadOnly penUpTimer As Timer
        Private ReadOnly hotSyncButtonReleaseTimer As Timer
        Private ReadOnly soundPlayer As New SoundPlayer()
        Private soundWaveStream As MemoryStream
        Private soundPlaying As Boolean
        Private currentSoundFrequency As Double
        Private currentSoundDuty As Double
        Private ReadOnly romBytes As Byte()
        Private ReadOnly statePath As String
        Private ReadOnly hotSyncPath As String
        Private ReadOnly hotSyncTracePath As String
        Private ReadOnly irdaCapturePath As String
        Private ReadOnly irdaTracePath As String
        Private ReadOnly irdaBeamLogPath As String
        Private cradleMenuItem As ToolStripMenuItem
        Private ReadOnly displayModeMenuItems As New List(Of ToolStripMenuItem)
        Private nativeReady As Boolean
        Private nativeSlices As UInteger
        Private autoRunTicks As Integer
        Private lastAutoLcdWriteCount As UInteger
        Private lastAutoLcdBase As UInteger
        Private lastAutoLcdPitch As UShort
        Private lastAutoLcdPanel As Byte
        Private lastAutoLcdPan As Byte
        Private lastAutoLcdTick As Long
        Private lastNativeWallTick As Long
        Private lastTouchX As Integer
        Private lastTouchY As Integer
        Private lastPenDown As Boolean
        Private lastTouchLogTick As Long
        Private powerWakeTouchActive As Boolean
        Private uartTxLogBuffer As New List(Of Byte)
        Private uartSlpRxBuffer As New List(Of Byte)
        Private irdaFrameBuffer As New List(Of Byte)
        Private lastUartLogTick As Long
        Private irdaInFrame As Boolean
        Private irdaEscaped As Boolean
        Private irdaRespondedInDiscovery As Boolean
        Private irdaLastDiscoverySource As UInteger
        Private irdaConnectionAddress As Byte
        Private irdaConnectedResponseAddress As Byte
        Private irdaConnectedPeer As UInteger
        Private irdaReceiveNext As Byte
        Private irdaTransmitNext As Byte
        Private irdaLastInformationResponse As Byte()
        Private irdaLastInformationResponseLabel As String = ""
        Private irdaObexObjectName As String = ""
        Private irdaObexObjectType As String = ""
        Private ReadOnly irdaObexBody As New List(Of Byte)
        Private ReadOnly irdaObexPacket As New List(Of Byte)
        Private irdaObexExpectedLength As Integer
        Private irdaObexClientLsap As Byte
        Private pendingIrdaResponse As Byte()
        Private pendingIrdaResponseLabel As String = ""
        Private pendingIrdaResponseDueTick As Long
        Private ReadOnly pendingIrdaResponses As New Queue(Of IrdaPendingResponse)
        Private irdaTxQuietSinceTick As Long
        Private irdaBeamState As IrdaBeamSendState = IrdaBeamSendState.Idle
        Private irdaBeamPeerAddress As UInteger
        Private irdaBeamConnectionAddress As Byte
        Private irdaBeamReceiveNext As Byte
        Private irdaBeamTransmitNext As Byte
        Private irdaBeamFileName As String = ""
        Private irdaBeamFileBytes As Byte()
        Private irdaBeamOffset As Integer
        Private irdaBeamLastProgressTick As Long
        Private irdaBeamDiscoverySlot As Integer
        Private irdaBeamEmptyEndBodySent As Boolean
        Private cmpHandshakeStarted As Boolean
        Private hostPadpTxId As Byte = 1
        Private cmpInitTxId As Byte
        Private dlpReadUserInfoSent As Boolean
        Private dlpReadUserInfoQueued As Boolean
        Private dlpReadUserInfoDueTick As Long
        Private dlpWriteUserInfoSent As Boolean
        Private ReadOnly pendingInstalls As New Queue(Of PalmDbImage)
        Private pendingInstall As PalmDbImage
        Private installState As HotSyncInstallState = HotSyncInstallState.Idle
        Private installDbId As Byte
        Private installResourceIndex As Integer
        Private installAwaitingTxId As Byte
        Private installAppBlockDone As Boolean
        Private installSortBlockDone As Boolean
        Private pendingPadpPayload As Byte()
        Private pendingPadpLabel As String = ""
        Private pendingPadpTxId As Byte
        Private pendingPadpOffset As Integer
        Private pendingPadpActive As Boolean
        Private rxPadpActive As Boolean
        Private rxPadpTxId As Byte
        Private rxPadpExpectedSize As Integer
        Private ReadOnly rxPadpPayload As New List(Of Byte)
        Private Const VerboseSerialLog As Boolean = False
        Private Const EnableHotSyncTrace As Boolean = False
        Private Const EnableIrdaTrace As Boolean = False
        Private Const IrdaSirBof As Byte = &HC0
        Private Const IrdaSirEof As Byte = &HC1
        Private Const IrdaSirEscape As Byte = &H7D
        Private Const IrdaBroadcastAddress As Byte = &HFF
        Private Const IrdaUiControl As Byte = &H3F
        Private Const IrdaDiscoveryResponseAddress As Byte = &HFE
        Private Const IrdaXidResponseControl As Byte = &HBF
        Private Const IrdaXidFormat As Byte = &H1
        Private Const IrdaSnrmControl As Byte = &H93
        Private Const IrdaUaResponseControl As Byte = &H73
        Private Const IrdaDiscControl As Byte = &H53
        Private Const IrdaRrPollFinalControl As Byte = &H11
        Private Const IrdaFinalBit As Byte = &H10
        Private Const IrdaControlBit As Byte = &H80
        Private Const IrdaLmpConnectCommand As Byte = &H1
        Private Const IrdaLmpConnectConfirm As Byte = &H81
        Private Const IrdaIasGetValueByClass As Byte = &H4
        Private Const IrdaIasLast As Byte = &H80
        Private Const IrdaIasSuccess As Byte = &H0
        Private Const IrdaIasInteger As Byte = &H1
        Private Const IrdaObexLsap As Byte = &H2
        Private Const IrdaHostAddress As UInteger = &H45535032UI ' "ESP2"
        Private Const IrdaTurnaroundDelayMs As Long = 20
        Private Const IrdaConnectedTurnaroundDelayMs As Long = 0
        Private Const IrdaTxQuietBeforeResponseMs As Long = 2
        Private Const IrdaCyclesPerPoll As Integer = 100
        Private Const IrdaBeamBodyChunkSize As Integer = 40
        Private Const IrdaBeamDiscoveryRetryMs As Long = 250
        Private Const IrdaBeamTimeoutMs As Long = 15000
        Private Const PadpChunkSize As Integer = 200
        Private Const PalmMemoMaxBytes As Integer = 4096
        Private Const DlpCmdReadUserInfo As Byte = &H10
        Private Const DlpCmdWriteUserInfo As Byte = &H11
        Private Const DlpCmdReadSysInfo As Byte = &H12
        Private Const DlpCmdReadStorageInfo As Byte = &H15
        Private Const DlpCmdCreateDB As Byte = &H18
        Private Const DlpCmdCloseDB As Byte = &H19
        Private Const DlpCmdDeleteDB As Byte = &H1A
        Private Const DlpCmdWriteAppBlock As Byte = &H1C
        Private Const DlpCmdWriteSortBlock As Byte = &H1E
        Private Const DlpCmdWriteRecord As Byte = &H21
        Private Const DlpCmdWriteResource As Byte = &H24
        Private Const DlpCmdEndOfSync As Byte = &H2F
        Private Const DlpArgFirstId As Byte = &H20
        Private Const SleepTimerStepMs As Long = 10
        Private Const SleepCatchupMaxMs As Long = 250
        Private Const SleepWakeCycles As Integer = 60000
        Private Const SoundSampleRate As Integer = 22050
        Private Const UseRawAdcTouch As Boolean = True
        Private Const SyncAdsAfterTouch As Boolean = False
        Private Const TouchSwapAxes As Boolean = False
        Private Const TouchInvertX As Boolean = False
        Private Const TouchInvertY As Boolean = False
        Private Const TouchOffsetX As Integer = 0
        Private Const TouchOffsetY As Integer = 0
        Private Const TouchScaleXPercent As Double = 100.0
        Private Const TouchScaleYPercent As Double = 100.0
        Private Const TouchRawXMin As Integer = 3800
        Private Const TouchRawXMax As Integer = 300
        Private Const TouchRawYMin As Integer = 3800
        Private Const TouchRawYMax As Integer = 300
        Private Const TouchHoldMs As Integer = 0

        Private Enum HotSyncInstallState
            Idle
            DeleteSent
            CreateSent
            AppBlockSent
            SortBlockSent
            WritingResource
            CloseSent
            EndSent
            Done
            Failed
        End Enum

        Private NotInheritable Class IrdaPendingResponse
            Public Sub New(frameBytes As Byte(), labelText As String, dueTick As Long)
                Frame = frameBytes
                Label = labelText
                Due = dueTick
            End Sub

            Public ReadOnly Frame As Byte()
            Public ReadOnly Label As String
            Public ReadOnly Due As Long
        End Class

        Private Enum IrdaBeamSendState
            Idle
            Discover
            Snrm
            IasConnect
            IasQuery
            ObexLmpConnect
            ObexConnect
            ObexPutMeta
            ObexPutBody
            ObexDisconnect
            LinkDisconnect
        End Enum

        Private Const KeyBitPower As UShort = &H1US
        Private Const KeyBitPageUp As UShort = &H2US
        Private Const KeyBitPageDown As UShort = &H4US
        Private Const KeyBitHard1 As UShort = &H8US
        Private Const KeyBitHard2 As UShort = &H10US
        Private Const KeyBitHard3 As UShort = &H20US
        Private Const KeyBitHard4 As UShort = &H40US
        Private Const KeyBitContrast As UShort = &H200US

        Public Sub New()
            Text = $"ESP32-PALM {PalmConfig.ProfileName}"
            StartPosition = FormStartPosition.CenterScreen
            Size = New Size(382, 660)
            MinimumSize = New Size(382, 620)

            Dim romPath = ResolveRuntimeFile(PalmConfig.RomFileName)
            statePath = Path.Combine(AppContext.BaseDirectory, PalmConfig.StateFileName)
            hotSyncPath = Path.Combine(AppContext.BaseDirectory, "HotSync")
            hotSyncTracePath = Path.Combine(AppContext.BaseDirectory, "HotSyncTrace.log")
            irdaCapturePath = Path.Combine(hotSyncPath, "IrDA")
            irdaTracePath = Path.Combine(AppContext.BaseDirectory, "IrDATrace.log")
            irdaBeamLogPath = Path.Combine(AppContext.BaseDirectory, "IrDABeam.log")
            romBytes = File.ReadAllBytes(romPath)
            memory = New PalmMemory(romPath, CInt(PalmConfig.RamLogicalSize))

            Dim deviceMenu = BuildDeviceMenu()
            deviceMenu.Dock = DockStyle.Top
            MainMenuStrip = deviceMenu
            Controls.Add(deviceMenu)

            Dim root As New TableLayoutPanel With {
                .Dock = DockStyle.Fill,
                .ColumnCount = 1,
                .RowCount = 1,
                .Padding = New Padding(10, deviceMenu.Height + 10, 10, 10)
            }
            root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))
            Controls.Add(root)

            devicePanel = New FlowLayoutPanel With {
                .Dock = DockStyle.Fill,
                .FlowDirection = FlowDirection.TopDown,
                .WrapContents = False,
                .AutoScroll = False
            }
            AddHandler devicePanel.Resize, AddressOf DevicePanel_Resize
            root.Controls.Add(devicePanel, 0, 0)

            lcdPanel = New LcdPanel With {.Margin = New Padding(0, 6, 0, 14)}
            AddHandler lcdPanel.PenChanged, AddressOf LcdPanel_PenChanged
            devicePanel.Controls.Add(lcdPanel)

            hardwarePanel = New HardwareButtonPanel With {.Margin = New Padding(0, 0, 0, 10)}
            AddHandler hardwarePanel.ButtonChanged, AddressOf HardwareButtonPanel_ButtonChanged
            devicePanel.Controls.Add(hardwarePanel)

            autoRunTimer = New Timer With {.Interval = 25}
            AddHandler autoRunTimer.Tick, AddressOf AutoRunTimer_Tick
            penUpTimer = New Timer With {.Interval = 120}
            AddHandler penUpTimer.Tick, AddressOf PenUpTimer_Tick
            hotSyncButtonReleaseTimer = New Timer With {.Interval = 650}
            AddHandler hotSyncButtonReleaseTimer.Tick, AddressOf HotSyncButtonReleaseTimer_Tick
            AddHandler FormClosing, AddressOf MainForm_FormClosing

            PrintHeader()
            RefreshDebugStatus()
            TryRestorePersistentState()
            StartEmulationByDefault()
            CenterDevicePanelContent()
        End Sub

        Private Sub DevicePanel_Resize(sender As Object, e As EventArgs)
            CenterDevicePanelContent()
        End Sub

        Private Sub CenterDevicePanelContent()
            If devicePanel Is Nothing Then Return

            Dim contentWidth = PalmConfig.DigitizerWidth * 2
            Dim leftPadding = Math.Max(0, (devicePanel.ClientSize.Width - contentWidth) \ 2)
            devicePanel.Padding = New Padding(leftPadding, 0, 0, 0)
        End Sub

        Private Function BuildDeviceMenu() As MenuStrip
            Dim menu As New MenuStrip()
            Dim deviceItem As New ToolStripMenuItem("Device")
            deviceItem.DropDownItems.Add(CreateMenuItem("Install PRC/PDB...", AddressOf InstallPrcButton_Click))
            deviceItem.DropDownItems.Add(CreateMenuItem("Beam File to Palm...", AddressOf BeamFileToPalmMenuItem_Click))
            If PalmConfig.ActiveHardwareProfile = PalmConfig.HardwareProfile.IIIcExperimental Then
                cradleMenuItem = CreateMenuItem("In Cradle", AddressOf InCradleMenuItem_Click)
                cradleMenuItem.CheckOnClick = True
                deviceItem.DropDownItems.Add(cradleMenuItem)
            End If
            deviceItem.DropDownItems.Add(BuildDisplayModeMenu())
            deviceItem.DropDownItems.Add(CreateMenuItem("Backup RAM State...", AddressOf BackupRamStateMenuItem_Click))
            deviceItem.DropDownItems.Add(CreateMenuItem("Restore RAM State...", AddressOf RestoreRamStateMenuItem_Click))
            deviceItem.DropDownItems.Add(New ToolStripSeparator())
            deviceItem.DropDownItems.Add(CreateMenuItem("Power", AddressOf PowerMenuItem_Click))
            deviceItem.DropDownItems.Add(CreateMenuItem("Reset", AddressOf ResetButton_Click))
            menu.Items.Add(deviceItem)
            Return menu
        End Function

        Private Function BuildDisplayModeMenu() As ToolStripMenuItem
            Dim displayItem As New ToolStripMenuItem("Display Mode")
            displayItem.DropDownItems.Add(CreateDisplayModeMenuItem("Normal LCD", LcdPanel.DisplayRenderMode.NormalMono))
            displayItem.DropDownItems.Add(CreateDisplayModeMenuItem("Inverted Green Backlight", LcdPanel.DisplayRenderMode.InvertedGreenBacklight))
            Return displayItem
        End Function

        Private Function CreateDisplayModeMenuItem(text As String, mode As LcdPanel.DisplayRenderMode) As ToolStripMenuItem
            Dim item As New ToolStripMenuItem(text) With {
                .Tag = mode,
                .Checked = mode = LcdPanel.DisplayRenderMode.NormalMono
            }
            AddHandler item.Click, AddressOf DisplayModeMenuItem_Click
            displayModeMenuItems.Add(item)
            Return item
        End Function

        Private Shared Function CreateMenuItem(text As String, handler As EventHandler) As ToolStripMenuItem
            Dim item As New ToolStripMenuItem(text)
            AddHandler item.Click, handler
            Return item
        End Function


        Private Sub DisplayModeMenuItem_Click(sender As Object, e As EventArgs)
            Dim item = TryCast(sender, ToolStripMenuItem)
            If item Is Nothing OrElse item.Tag Is Nothing Then Return

            Dim mode = DirectCast(item.Tag, LcdPanel.DisplayRenderMode)
            lcdPanel.RenderMode = mode
            UpdateDisplayModeChecks(mode)
        End Sub

        Private Sub UpdateDisplayModeChecks(mode As LcdPanel.DisplayRenderMode)
            For Each item In displayModeMenuItems
                item.Checked = DirectCast(item.Tag, LcdPanel.DisplayRenderMode) = mode
            Next
        End Sub

        Private Sub PrintHeader()
            Append($"Ready: ROM {memory.RomSize \ 1024} KB, RAM {PalmConfig.RamLogicalSize \ 1024UI} KB")
        End Sub

        Private Shared Function ResolveRuntimeFile(fileName As String) As String
            Dim candidates = {
                Path.Combine(AppContext.BaseDirectory, fileName),
                Path.Combine(Environment.CurrentDirectory, fileName),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..\..\..\..\", fileName))
            }

            For Each candidate In candidates
                If File.Exists(candidate) Then Return candidate
            Next

            Throw New FileNotFoundException($"Could not find {fileName}. Put it next to PalmDesktopHarness.exe.", fileName)
        End Function

        Private Sub TryRestorePersistentState()
            If Not PalmConfig.AutoRestorePersistentState Then
                Append($"Persistent state auto-restore disabled for {PalmConfig.ProfileName}.")
                Return
            End If

            If Not File.Exists(statePath) Then Return

            If Not nativeReady Then
                nativeReady = NativeMusashi.palm_native_init(romBytes, CUInt(romBytes.Length), PalmConfig.RamLogicalSize) <> 0
            End If
            If Not nativeReady Then
                Append("Persistent state restore skipped: native init failed.")
                Return
            End If

            Try
                Dim bytes = File.ReadAllBytes(statePath)
                If NativeMusashi.palm_native_load_state(bytes, CUInt(bytes.Length)) <> 0 Then
                    Append($"Persistent state restored: {statePath}")
                    RefreshDebugStatus()
                    UpdateNativeLcdPreview()
                Else
                    Append($"Persistent state ignored: incompatible or invalid file {statePath}")
                End If
            Catch ex As Exception
                Append($"Persistent state restore failed: {ex.Message}")
            End Try
        End Sub

        Private Sub MainForm_FormClosing(sender As Object, e As FormClosingEventArgs)
            StopSound()
            SavePersistentState()
        End Sub

        Private Sub StartEmulationByDefault()
            If Not nativeReady Then
                nativeReady = NativeMusashi.palm_native_init(romBytes, CUInt(romBytes.Length), PalmConfig.RamLogicalSize) <> 0
                Append(If(nativeReady, "Native Musashi initialized.", "Native Musashi init failed."))
            End If
            If Not nativeReady Then Return

            If NativeMusashi.palm_native_is_asleep() <> 0 Then
                NativeMusashi.palm_native_set_power_button(1)
                For i = 1 To 8
                    NativeMusashi.palm_native_execute(10000)
                Next
                NativeMusashi.palm_native_set_power_button(0)
            End If

            autoRunTimer.Start()
            lastNativeWallTick = Environment.TickCount64
            Append("Auto run started.")
        End Sub

        Private Sub SavePersistentState()
            If Not nativeReady Then Return

            autoRunTimer.Stop()
            penUpTimer.Stop()
            hotSyncButtonReleaseTimer.Stop()
            NativeMusashi.palm_native_set_trace_enabled(0)
            NativeMusashi.palm_native_set_pen(0, 0, 0)
            NativeMusashi.palm_native_set_cradle_button(0)

            If SaveNativeStateTo(statePath, True) Then Append($"Persistent state saved: {statePath}")
        End Sub

        Private Function SaveNativeStateTo(path As String, enterSleep As Boolean) As Boolean
            If Not nativeReady Then Return False

            If enterSleep Then TryEnterSleepForSave()

            Dim stateSize = NativeMusashi.palm_native_state_size()
            If stateSize = 0UI Then
                Append("RAM state save failed: native state size is zero.")
                Return False
            End If

            Try
                Dim bytes(CInt(stateSize) - 1) As Byte
                If NativeMusashi.palm_native_save_state(bytes, stateSize) <> 0 Then
                    File.WriteAllBytes(path, bytes)
                    Return True
                End If

                Append("RAM state save failed: native save returned false.")
            Catch ex As Exception
                Append($"RAM state save failed: {ex.Message}")
            End Try
            Return False
        End Function

        Private Sub TryEnterSleepForSave()
            If NativeMusashi.palm_native_is_asleep() = 0 Then
                NativeMusashi.palm_native_set_power_button(1)
                For i = 1 To 6
                    NativeMusashi.palm_native_execute(10000)
                Next
                NativeMusashi.palm_native_set_power_button(0)
            End If

            For i = 1 To 80
                If NativeMusashi.palm_native_is_asleep() <> 0 Then Exit For
                NativeMusashi.palm_native_execute(20000)
            Next
        End Sub

        Private Sub RefreshDebugStatus()
            If nativeReady Then UpdateNativeLcdPreview()
        End Sub

        Private Sub UpdateNativeLcdPreview()
            UpdateSleepVisualState()

            Dim baseAddress = NativeMusashi.palm_native_lcd_start()
            Dim width = CInt(NativeMusashi.palm_native_lcd_width())
            Dim height = CInt(NativeMusashi.palm_native_lcd_height())
            Dim pitch = CInt(NativeMusashi.palm_native_lcd_pitch())
            Dim bpp = 1 << (NativeMusashi.palm_native_lcd_panel() And 3)
            Dim pan = CInt(NativeMusashi.palm_native_lcd_pan())
            Dim lcdContrast = PalmConfig.DefaultLcdContrastRegister
            If PalmConfig.UseLcdContrastRegister Then lcdContrast = NativeMusashi.palm_native_lcd_contrast()
            lcdPanel.ContrastValue = lcdContrast

            If baseAddress = 0UI OrElse width <= 0 OrElse height <= 0 OrElse pitch <= 0 OrElse width > 320 OrElse height > 320 Then
                Return
            End If

            Dim byteCount = pitch * height
            Dim bytes(byteCount - 1) As Byte
            Dim copied = NativeMusashi.palm_native_copy_memory(baseAddress, bytes, CUInt(byteCount))
            If copied <> CUInt(byteCount) Then Return

            Dim palette As UInteger() = Nothing
            If PalmConfig.UseColorLcdPalette Then
                Dim entries = Math.Max(1, 1 << Math.Min(bpp, 8))
                ReDim palette(entries - 1)
                For i = 0 To entries - 1
                    palette(i) = NativeMusashi.palm_native_lcd_palette(CByte(i))
                Next
            End If

            lcdPanel.UpdateFrame(bytes, width, height, pitch, bpp, pan, palette)
            NativeMusashi.palm_native_lcd_mark_clean()
        End Sub

        Private Sub UpdateSleepVisualState()
            If Not nativeReady Then
                lcdPanel.DisplayAsleep = False
                Return
            End If

            lcdPanel.DisplayAsleep = NativeMusashi.palm_native_is_asleep() <> 0
        End Sub

        Private Sub InstallPrcButton_Click(sender As Object, e As EventArgs)
            If Not nativeReady Then
                InitCpuButton_Click(Me, EventArgs.Empty)
                If Not nativeReady Then Return
            End If

            Using dialog As New OpenFileDialog With {
                .Title = "Install Palm database",
                .Filter = "Palm database (*.prc;*.pdb;*.pqa)|*.prc;*.pdb;*.pqa|All files (*.*)|*.*",
                .CheckFileExists = True,
                .Multiselect = True
            }
                If dialog.ShowDialog(Me) <> DialogResult.OK Then Return

                Try
                    Dim queued = 0
                    For Each fileName In dialog.FileNames
                        Dim bytes = File.ReadAllBytes(fileName)
                        Dim image = PalmDbImage.Parse(bytes, fileName)
                        pendingInstalls.Enqueue(image)
                        queued += 1
                        Append($"Queued HotSync install: {image.Name} ({image.ItemCount} {image.ItemKindName}s).")
                    Next

                    If installState = HotSyncInstallState.Done OrElse installState = HotSyncInstallState.Failed Then
                        installState = HotSyncInstallState.Idle
                    End If
                    If pendingInstall Is Nothing AndAlso installState = HotSyncInstallState.Idle Then
                        PrepareNextPendingInstall()
                    End If

                    Append($"Install queue ready: {queued} file{If(queued = 1, "", "s")}. Starting cradle HotSync.")
                    PulseCradleHotSync()
                Catch ex As Exception
                    Append($"Install failed: {ex.Message}")
                End Try
            End Using
        End Sub

        Private Sub BeamFileToPalmMenuItem_Click(sender As Object, e As EventArgs)
            If Not nativeReady Then
                InitCpuButton_Click(Me, EventArgs.Empty)
                If Not nativeReady Then Return
            End If

            Using dialog As New OpenFileDialog With {
                .Title = "Beam file to Palm",
                .Filter = "Palm/beamable files (*.prc;*.pdb;*.pqa;*.bas;*.txt;*.npd)|*.prc;*.pdb;*.pqa;*.bas;*.txt;*.npd|All files (*.*)|*.*",
                .CheckFileExists = True,
                .Multiselect = False
            }
                If dialog.ShowDialog(Me) <> DialogResult.OK Then Return

                Try
                    StartIrdaBeamSend(dialog.FileName)
                Catch ex As Exception
                    Append($"IrDA beam send failed: {ex.Message}")
                    ResetIrdaBeamSender()
                End Try
            End Using
        End Sub

        Private Sub BackupRamStateMenuItem_Click(sender As Object, e As EventArgs)
            If Not nativeReady Then
                InitCpuButton_Click(Me, EventArgs.Empty)
                If Not nativeReady Then Return
            End If

            If MessageBox.Show(Me,
                               "Backup will pause the emulator briefly and save the current Palm RAM/device state. Continue?",
                               "Backup RAM State",
                               MessageBoxButtons.OKCancel,
                               MessageBoxIcon.Question,
                               MessageBoxDefaultButton.Button2) <> DialogResult.OK Then Return

            Dim backupDir = Path.Combine(AppContext.BaseDirectory, "StateBackup")
            Directory.CreateDirectory(backupDir)
            Using dialog As New SaveFileDialog With {
                .Title = "Backup RAM state",
                .Filter = "Palm emulator state (*.bin)|*.bin|All files (*.*)|*.*",
                .InitialDirectory = backupDir,
                .FileName = $"{PalmConfig.ProfileName.Replace(" "c, "_"c)}_{DateTime.Now:yyyyMMdd_HHmmss}.bin",
                .OverwritePrompt = True
            }
                If dialog.ShowDialog(Me) <> DialogResult.OK Then Return

                Dim resumeAutoRun = autoRunTimer.Enabled
                autoRunTimer.Stop()
                Try
                    If SaveNativeStateTo(dialog.FileName, False) Then
                        Append($"RAM state backup saved: {dialog.FileName}")
                    End If
                Finally
                    If resumeAutoRun Then autoRunTimer.Start()
                End Try
            End Using
        End Sub

        Private Sub RestoreRamStateMenuItem_Click(sender As Object, e As EventArgs)
            If MessageBox.Show(Me,
                               "Restore will replace the current running Palm RAM/device state. Any unsaved work in the emulator will be lost. Continue?",
                               "Restore RAM State",
                               MessageBoxButtons.OKCancel,
                               MessageBoxIcon.Warning,
                               MessageBoxDefaultButton.Button2) <> DialogResult.OK Then Return

            Dim backupDir = Path.Combine(AppContext.BaseDirectory, "StateBackup")
            Using dialog As New OpenFileDialog With {
                .Title = "Restore RAM state",
                .Filter = "Palm emulator state (*.bin)|*.bin|All files (*.*)|*.*",
                .InitialDirectory = If(Directory.Exists(backupDir), backupDir, AppContext.BaseDirectory),
                .CheckFileExists = True
            }
                If dialog.ShowDialog(Me) <> DialogResult.OK Then Return

                If Not nativeReady Then
                    nativeReady = NativeMusashi.palm_native_init(romBytes, CUInt(romBytes.Length), PalmConfig.RamLogicalSize) <> 0
                    If Not nativeReady Then
                        Append("RAM state restore failed: native init failed.")
                        Return
                    End If
                End If

                Dim resumeAutoRun = autoRunTimer.Enabled
                autoRunTimer.Stop()
                penUpTimer.Stop()
                hotSyncButtonReleaseTimer.Stop()
                StopSound()
                Try
                    Dim bytes = File.ReadAllBytes(dialog.FileName)
                    If NativeMusashi.palm_native_load_state(bytes, CUInt(bytes.Length)) <> 0 Then
                        nativeSlices = 0UI
                        autoRunTicks = 0
                        lastAutoLcdTick = 0
                        lastNativeWallTick = Environment.TickCount64
                        pendingInstall = Nothing
                        pendingInstalls.Clear()
                        installState = HotSyncInstallState.Idle
                        installAppBlockDone = False
                        installSortBlockDone = False
                        RefreshDebugStatus()
                        UpdateNativeLcdPreview()
                        Append($"RAM state restored: {dialog.FileName}")
                    Else
                        Append($"RAM state restore failed: incompatible or invalid file {dialog.FileName}")
                    End If
                Catch ex As Exception
                    Append($"RAM state restore failed: {ex.Message}")
                Finally
                    If resumeAutoRun OrElse nativeReady Then autoRunTimer.Start()
                End Try
            End Using
        End Sub

        Private Sub ResetButton_Click(sender As Object, e As EventArgs)
            If Not nativeReady Then
                InitCpuButton_Click(Me, EventArgs.Empty)
                If Not nativeReady Then Return
            End If

            Dim resumeAutoRun = autoRunTimer.Enabled
            autoRunTimer.Stop()
            penUpTimer.Stop()
            hotSyncButtonReleaseTimer.Stop()
            StopSound()
            NativeMusashi.palm_native_set_pen(0, 0, 0)
            NativeMusashi.palm_native_set_button_bits(&HFFFFUS, 0)
            NativeMusashi.palm_native_set_power_button(0)
            NativeMusashi.palm_native_set_cradle_button(0)
            NativeMusashi.palm_native_set_in_cradle(0)
            If cradleMenuItem IsNot Nothing Then cradleMenuItem.Checked = False
            NativeMusashi.palm_native_warm_reset()
            nativeSlices = 0UI
            autoRunTicks = 0
            lastAutoLcdTick = 0
            lastNativeWallTick = Environment.TickCount64
            pendingInstall = Nothing
            pendingInstalls.Clear()
            installState = HotSyncInstallState.Idle
            installAppBlockDone = False
            installSortBlockDone = False
            pendingPadpActive = False
            pendingPadpPayload = Nothing
            uartTxLogBuffer.Clear()
            uartSlpRxBuffer.Clear()
            ResetPadpReceive()
            For i = 1 To 12
                NativeMusashi.palm_native_execute(20000)
            Next
            Append("Palm warm reset.")
            RefreshDebugStatus()
            If resumeAutoRun Then autoRunTimer.Start()
        End Sub

        Private Sub InitCpuButton_Click(sender As Object, e As EventArgs)
            Try
                nativeReady = NativeMusashi.palm_native_init(romBytes, CUInt(romBytes.Length), PalmConfig.RamLogicalSize) <> 0
                If nativeReady AndAlso cradleMenuItem IsNot Nothing Then
                    cradleMenuItem.Checked = False
                    NativeMusashi.palm_native_set_in_cradle(0)
                End If
                nativeSlices = 0UI
                lastAutoLcdTick = 0
                Append(If(nativeReady, "Native Musashi initialized.", "Native Musashi init failed."))
                RefreshDebugStatus()
            Catch ex As DllNotFoundException
                Append("PalmMusashi.dll was not found. Build NativeMusashi first.")
            Catch ex As EntryPointNotFoundException
                Append($"PalmMusashi.dll entry point missing: {ex.Message}")
            End Try
        End Sub

        Private Sub StepCpuButton_Click(sender As Object, e As EventArgs)
            RunNativeSlices(100, 2000)
        End Sub

        Private Sub OneCpuButton_Click(sender As Object, e As EventArgs)
            RunNativeSlices(1, 2000)
        End Sub

        Private Sub RunCpuButton_Click(sender As Object, e As EventArgs)
            RunNativeSlices(10000, 2000, True)
        End Sub

        Private Sub FastCpuButton_Click(sender As Object, e As EventArgs)
            RunNativeSlices(1000, 100000, False)
        End Sub

        Private Sub AutoRunButton_Click(sender As Object, e As EventArgs)
            If Not nativeReady Then
                InitCpuButton_Click(Me, EventArgs.Empty)
                If Not nativeReady Then Return
            End If

            autoRunTimer.Enabled = Not autoRunTimer.Enabled
            NativeMusashi.palm_native_set_trace_enabled(0)
            Append(If(autoRunTimer.Enabled, "Auto run started.", "Auto run stopped."))
        End Sub

        Private Sub PowerMenuItem_Click(sender As Object, e As EventArgs)
            If nativeReady AndAlso NativeMusashi.palm_native_is_asleep() <> 0 Then
                WakeSleepingPalmWithPowerButton()
                Return
            End If

            SendButtonBitsToNative(KeyBitPower, True, "Power")
            SendButtonBitsToNative(KeyBitPower, False, "Power")
        End Sub

        Private Sub InCradleMenuItem_Click(sender As Object, e As EventArgs)
            If Not nativeReady Then
                InitCpuButton_Click(Me, EventArgs.Empty)
                If Not nativeReady Then Return
            End If

            Dim inCradle = cradleMenuItem IsNot Nothing AndAlso cradleMenuItem.Checked
            NativeMusashi.palm_native_set_in_cradle(If(inCradle, 1, 0))
            Append(If(inCradle, "Palm IIIc placed in cradle.", "Palm IIIc removed from cradle."))
        End Sub

        Private Sub BrightnessMenuItem_Click(sender As Object, e As EventArgs)
            If Not nativeReady Then
                InitCpuButton_Click(Me, EventArgs.Empty)
                If Not nativeReady Then Return
            End If

            NativeMusashi.palm_native_show_brightness_adjust()
            If autoRunTimer.Enabled Then
                For i = 1 To 12
                    NativeMusashi.palm_native_execute(10000)
                Next
                nativeSlices += 12UI
                UpdateNativeLcdPreview()
            Else
                RunNativeSlices(100, 2000, False)
            End If
        End Sub

        Private Sub HardwareButtonPanel_ButtonChanged(bits As UShort, down As Boolean, label As String)
            SendButtonBitsToNative(bits, down, label)
        End Sub


        Private Sub AutoRunTimer_Tick(sender As Object, e As EventArgs)
            If Not nativeReady Then
                autoRunTimer.Stop()
                Return
            End If

            Dim sleepWakePending = If(PalmConfig.PauseCpuWhileSleeping, AdvanceSleepingClock(), False)
            Dim skipSleepingCpu = PalmConfig.PauseCpuWhileSleeping AndAlso
                                  NativeMusashi.palm_native_is_asleep() <> 0 AndAlso
                                  Not sleepWakePending

            If Not skipSleepingCpu Then
                For i = 1 To PalmConfig.NativeAutoRunSlicesPerTick
                    ExecuteAutoRunSlice()
                Next
                nativeSlices += CUInt(PalmConfig.NativeAutoRunSlicesPerTick)
            End If
            autoRunTicks += 1
            PollNativeUart(True)
            ServiceHotSyncHost()
            UpdateSound()
            UpdateSleepVisualState()

            If ShouldRefreshAutoLcd() Then UpdateNativeLcdPreview()
        End Sub

        Private Sub ExecuteAutoRunSlice()
            Dim cyclesRemaining = PalmConfig.NativeAutoRunCyclesPerSlice
            If NativeMusashi.palm_native_uart_is_irda() = 0UI Then
                NativeMusashi.palm_native_execute(cyclesRemaining)
                PollNativeUart(False)
                ServiceHotSyncHost()
                Return
            End If

            While cyclesRemaining > 0
                PollNativeUart(False)
                Dim stepCycles = Math.Min(IrdaCyclesPerPoll, cyclesRemaining)
                NativeMusashi.palm_native_execute(stepCycles)
                PollNativeUart(False)
                ServiceHotSyncHost()
                cyclesRemaining -= stepCycles
            End While
        End Sub

        Private Sub UpdateSound()
            If Not nativeReady Then
                StopSound()
                Return
            End If

            Dim enabled = NativeMusashi.palm_native_sound_enabled() <> 0
            Dim frequency = NativeMusashi.palm_native_sound_frequency()
            Dim duty = NativeMusashi.palm_native_sound_duty()
            If Not enabled OrElse frequency < 20.0 OrElse frequency > 20000.0 Then
                StopSound()
                Return
            End If

            If Not soundPlaying OrElse Math.Abs(frequency - currentSoundFrequency) > 2.0 OrElse
                Math.Abs(duty - currentSoundDuty) > 0.05 Then
                StartSquareWave(frequency, duty)
            End If
        End Sub

        Private Sub StartSquareWave(frequency As Double, duty As Double)
            Dim cycleCount = Math.Max(1, CInt(Math.Round(frequency * 0.2)))
            Dim sampleCount = Math.Max(64, CInt(Math.Round(cycleCount * SoundSampleRate / frequency)))
            Dim dataBytes = sampleCount * 2
            Dim stream = New MemoryStream(44 + dataBytes)

            WriteAscii(stream, "RIFF")
            WriteUInt32Le(stream, CUInt(36 + dataBytes))
            WriteAscii(stream, "WAVEfmt ")
            WriteUInt32Le(stream, 16UI)
            WriteUInt16Le(stream, 1US)
            WriteUInt16Le(stream, 1US)
            WriteUInt32Le(stream, CUInt(SoundSampleRate))
            WriteUInt32Le(stream, CUInt(SoundSampleRate * 2))
            WriteUInt16Le(stream, 2US)
            WriteUInt16Le(stream, 16US)
            WriteAscii(stream, "data")
            WriteUInt32Le(stream, CUInt(dataBytes))

            Dim highSamples = Math.Max(1, CInt(Math.Round(SoundSampleRate / frequency * Math.Max(0.02, Math.Min(0.98, duty)))))
            Dim periodSamples = Math.Max(2, CInt(Math.Round(SoundSampleRate / frequency)))
            For i = 0 To sampleCount - 1
                Dim phase = i Mod periodSamples
                Dim sample As Short = If(phase < highSamples, CShort(8000), CShort(-8000))
                WriteUInt16Le(stream, CUShort(CInt(sample) And &HFFFF))
            Next

            stream.Position = 0
            soundPlayer.Stop()
            If soundWaveStream IsNot Nothing Then soundWaveStream.Dispose()
            soundWaveStream = stream
            soundPlayer.Stream = soundWaveStream
            soundPlayer.PlayLooping()
            currentSoundFrequency = frequency
            currentSoundDuty = duty
            soundPlaying = True
        End Sub

        Private Sub StopSound()
            If Not soundPlaying AndAlso soundWaveStream Is Nothing Then Return

            soundPlayer.Stop()
            soundPlaying = False
            currentSoundFrequency = 0.0
            currentSoundDuty = 0.0
            If soundWaveStream IsNot Nothing Then
                soundWaveStream.Dispose()
                soundWaveStream = Nothing
            End If
        End Sub

        Private Shared Sub WriteAscii(stream As Stream, text As String)
            Dim bytes = Encoding.ASCII.GetBytes(text)
            stream.Write(bytes, 0, bytes.Length)
        End Sub

        Private Shared Sub WriteUInt16Le(stream As Stream, value As UShort)
            stream.WriteByte(CByte(value And &HFFUS))
            stream.WriteByte(CByte((value >> 8) And &HFFUS))
        End Sub

        Private Shared Sub WriteUInt32Le(stream As Stream, value As UInteger)
            stream.WriteByte(CByte(value And &HFFUI))
            stream.WriteByte(CByte((value >> 8) And &HFFUI))
            stream.WriteByte(CByte((value >> 16) And &HFFUI))
            stream.WriteByte(CByte((value >> 24) And &HFFUI))
        End Sub

        Private Function AdvanceSleepingClock() As Boolean
            Dim nowTick = Environment.TickCount64
            If lastNativeWallTick = 0 Then
                lastNativeWallTick = nowTick
                Return True
            End If

            Dim elapsed = nowTick - lastNativeWallTick
            lastNativeWallTick = nowTick
            If elapsed <= 0 OrElse NativeMusashi.palm_native_is_asleep() = 0 Then Return True

            elapsed = Math.Min(elapsed, SleepCatchupMaxMs)
            While elapsed > 0
                Dim stepMs = Math.Min(elapsed, SleepTimerStepMs)
                If NativeMusashi.palm_native_advance_time_ms(CUInt(stepMs)) <> 0 Then
                    NativeMusashi.palm_native_service_wake(SleepWakeCycles)
                    PollNativeUart(False)
                    ServiceHotSyncHost()
                    If NativeMusashi.palm_native_is_asleep() = 0 Then Return True
                End If
                elapsed -= stepMs
            End While

            Return NativeMusashi.palm_native_is_asleep() = 0
        End Function

        Private Sub PollNativeUart(forceLog As Boolean)
            Dim pending = NativeMusashi.palm_native_uart_tx_count()
            Dim hotSyncRouteEnabled = NativeMusashi.palm_native_uart_is_irda() = 0UI
            Dim irdaRouteEnabled = Not hotSyncRouteEnabled
            Dim nowTick = Environment.TickCount64
            If pending > 0UI Then
                If irdaRouteEnabled Then irdaTxQuietSinceTick = 0
                Dim buffer(CInt(Math.Min(256UI, pending)) - 1) As Byte
                Dim read = NativeMusashi.palm_native_uart_read_tx(buffer, CUInt(buffer.Length))
                For i = 0 To CInt(read) - 1
                    Dim value = buffer(i)
                    uartTxLogBuffer.Add(value)
                    If hotSyncRouteEnabled Then
                        uartSlpRxBuffer.Add(value)
                    Else
                        ProcessIrdaByte(value)
                    End If
                Next
                If Not hotSyncRouteEnabled Then
                    uartSlpRxBuffer.Clear()
                Else
                    ProcessPalmSerialFrames()
                End If
            End If

            If irdaRouteEnabled AndAlso NativeMusashi.palm_native_uart_tx_count() = 0UI AndAlso irdaTxQuietSinceTick = 0 Then
                irdaTxQuietSinceTick = nowTick
            End If
            If irdaRouteEnabled Then SendPendingIrdaResponse()
            ServiceIrdaBeamSender()

            If VerboseSerialLog AndAlso uartTxLogBuffer.Count > 0 AndAlso (forceLog OrElse nowTick - lastUartLogTick >= 250) Then
                Append($"UART TX {uartTxLogBuffer.Count}: {FormatBytes(uartTxLogBuffer)}")
                uartTxLogBuffer.Clear()
                lastUartLogTick = nowTick
            ElseIf Not VerboseSerialLog AndAlso uartTxLogBuffer.Count > 4096 Then
                uartTxLogBuffer.Clear()
                lastUartLogTick = nowTick
            End If
        End Sub

        Private Sub ProcessIrdaByte(value As Byte)
            Select Case value
                Case IrdaSirBof
                    irdaFrameBuffer.Clear()
                    irdaInFrame = True
                    irdaEscaped = False
                    Return
                Case IrdaSirEof
                    If irdaInFrame AndAlso irdaFrameBuffer.Count >= 4 Then
                        HandleIrdaFrame(irdaFrameBuffer.ToArray())
                        SendPendingIrdaResponse()
                    End If
                    irdaFrameBuffer.Clear()
                    irdaInFrame = False
                    irdaEscaped = False
                    Return
            End Select

            If Not irdaInFrame Then Return

            If irdaEscaped Then
                irdaFrameBuffer.Add(CByte(value Xor &H20))
                irdaEscaped = False
            ElseIf value = IrdaSirEscape Then
                irdaEscaped = True
            Else
                irdaFrameBuffer.Add(value)
            End If
        End Sub

        Private Sub StartIrdaBeamSend(filePath As String)
            If String.IsNullOrWhiteSpace(filePath) Then Return

            ResetIrdaBeamSender()
            ResetIrdaConnectionState()
            pendingIrdaResponse = Nothing
            pendingIrdaResponses.Clear()

            irdaBeamFileName = Path.GetFileName(filePath)
            irdaBeamFileBytes = File.ReadAllBytes(filePath)
            If Path.GetExtension(irdaBeamFileName).Equals(".txt", StringComparison.OrdinalIgnoreCase) Then
                irdaBeamFileBytes = NormalizeIrdaBeamTextBytes(irdaBeamFileBytes)
                If irdaBeamFileBytes.Length > PalmMemoMaxBytes Then
                    Throw New InvalidOperationException($"Memo text is too large ({irdaBeamFileBytes.Length} bytes). Limit is {PalmMemoMaxBytes} bytes.")
                End If
            End If
            irdaBeamConnectionAddress = &HC0
            irdaBeamReceiveNext = 0
            irdaBeamTransmitNext = 0
            irdaBeamOffset = 0
            irdaBeamDiscoverySlot = -1
            irdaBeamEmptyEndBodySent = False
            irdaBeamState = IrdaBeamSendState.Discover
            irdaBeamLastProgressTick = 0

            AppendIrdaBeamLog($"IrDA beam send starting: {irdaBeamFileName} ({irdaBeamFileBytes.Length} bytes).")
            AppendIrdaBeamLog("Open Beam Receive on the Palm if it is not already listening.")
            If NativeMusashi.palm_native_uart_is_irda() <> 0UI Then
                SendIrdaBeamDiscovery()
            Else
                AppendIrdaBeamLog("IrDA beam send armed; waiting for Palm OS to enable IrDA UART.")
            End If
        End Sub

        Private Sub ServiceIrdaBeamSender()
            If irdaBeamState = IrdaBeamSendState.Idle Then Return
            If NativeMusashi.palm_native_uart_is_irda() = 0UI Then Return

            Dim nowTick = Environment.TickCount64
            If irdaBeamState = IrdaBeamSendState.Discover Then
                If irdaBeamLastProgressTick = 0 OrElse nowTick - irdaBeamLastProgressTick >= IrdaBeamDiscoveryRetryMs Then
                    SendIrdaBeamDiscovery()
                End If
                Return
            End If

            If irdaBeamLastProgressTick <> 0 AndAlso nowTick - irdaBeamLastProgressTick < IrdaBeamTimeoutMs Then Return
            AppendIrdaBeamLog($"IrDA beam send timed out at {irdaBeamState}.")
            ResetIrdaBeamSender()
        End Sub

        Private Function HandleIrdaBeamSenderFrame(frame As Byte()) As Boolean
            If frame.Length < 2 Then Return False
            AppendIrdaBeamLog($"IrDA beam RX state={irdaBeamState} addr=${frame(0):X2} ctrl=${frame(1):X2} len={frame.Length}")

            If irdaBeamState = IrdaBeamSendState.Discover AndAlso
                frame.Length >= 13 AndAlso frame(0) = IrdaDiscoveryResponseAddress AndAlso frame(1) = IrdaXidResponseControl Then
                irdaBeamPeerAddress = ReadU32(frame, 3)
                irdaBeamLastProgressTick = Environment.TickCount64
                AppendIrdaTrace($"BEAM peer XID ${irdaBeamPeerAddress:X8}")
                AppendIrdaBeamLog($"IrDA beam peer found: ${irdaBeamPeerAddress:X8}")
                irdaBeamState = IrdaBeamSendState.Snrm
                SendIrdaBeamSnrm()
                Return True
            End If

            If irdaBeamState = IrdaBeamSendState.Snrm AndAlso frame.Length >= 2 AndAlso frame(1) = IrdaUaResponseControl Then
                irdaBeamReceiveNext = 0
                irdaBeamTransmitNext = 0
                irdaBeamLastProgressTick = Environment.TickCount64
                AppendIrdaBeamLog("IrDA beam link established; connecting IAS.")
                irdaBeamState = IrdaBeamSendState.IasConnect
                SendIrdaBeamInformation(BuildLmpConnectPayload(0, 1, 0), "IAS-CONNECT")
                Return True
            End If

            If frame(0) <> irdaBeamConnectionAddress AndAlso frame(0) <> (irdaBeamConnectionAddress Or 1) Then
                AppendIrdaTrace($"BEAM ignored frame state={irdaBeamState} addr=${frame(0):X2} {FormatBytes(frame)}")
                Return False
            End If

            Dim control = frame(1)
            If IsIrdaReceiveReadyControl(control) Then
                irdaBeamLastProgressTick = Environment.TickCount64
                Return True
            End If

            If control = IrdaDiscControl Then
                AppendIrdaBeamLog($"IrDA beam peer disconnected during {irdaBeamState}.")
                SendIrdaBeamControl(IrdaUaResponseControl, "BEAM-UA-DISC")
                ResetIrdaBeamSender()
                Return True
            End If

            If Not IsIrdaInformationControl(control) Then
                AppendIrdaTrace($"BEAM control state={irdaBeamState} ctrl=${control:X2} {FormatBytes(frame)}")
                Return True
            End If
            If frame.Length < 4 Then
                AppendIrdaTrace($"BEAM short I-frame state={irdaBeamState} {FormatBytes(frame)}")
                Return True
            End If

            Dim sendSequence = CByte((control >> 1) And &H7)
            If sendSequence = irdaBeamReceiveNext Then irdaBeamReceiveNext = CByte((irdaBeamReceiveNext + 1) And &H7)

            Dim dataLength = frame.Length - 4
            Dim data(dataLength - 1) As Byte
            Array.Copy(frame, 2, data, 0, dataLength)
            HandleIrdaBeamSenderPayload(data)
            Return True
        End Function

        Private Sub HandleIrdaBeamSenderPayload(data As Byte())
            irdaBeamLastProgressTick = Environment.TickCount64
            If data.Length = 0 Then Return

            Select Case irdaBeamState
                Case IrdaBeamSendState.IasConnect
                    If data.Length >= 4 AndAlso (data(0) And &H7F) = 1 AndAlso data(2) = IrdaLmpConnectConfirm Then
                        ' Palm OS 3.x/4.x OBEX receive uses the standard TinyTP LSAP 2.
                        ' Going directly keeps the sender small and avoids device-specific IAS quirks.
                        AppendIrdaBeamLog("IrDA beam IAS connected; connecting OBEX.")
                        irdaBeamState = IrdaBeamSendState.ObexLmpConnect
                        SendIrdaBeamInformation(BuildLmpConnectPayload(IrdaObexLsap, 3, &H10), "OBEX-LMP-CONNECT")
                    Else
                        AppendIrdaBeamLog($"IrDA beam unexpected IAS response: {FormatBytes(data.Take(Math.Min(data.Length, 16)).ToArray())}")
                    End If

                Case IrdaBeamSendState.IasQuery
                    If data.Length >= 4 Then
                        AppendIrdaBeamLog("IrDA beam IAS query answered; connecting OBEX.")
                        irdaBeamState = IrdaBeamSendState.ObexLmpConnect
                        SendIrdaBeamInformation(BuildLmpConnectPayload(IrdaObexLsap, 3, &H10), "OBEX-LMP-CONNECT")
                    End If

                Case IrdaBeamSendState.ObexLmpConnect
                    If data.Length >= 4 AndAlso data(2) = IrdaLmpConnectConfirm Then
                        AppendIrdaBeamLog("IrDA beam OBEX link connected.")
                        irdaBeamState = IrdaBeamSendState.ObexConnect
                        SendIrdaBeamInformation(WrapTinyTpObex(BuildObexConnectPacket()), "OBEX-CONNECT")
                    Else
                        AppendIrdaBeamLog($"IrDA beam unexpected OBEX link response: {FormatBytes(data.Take(Math.Min(data.Length, 16)).ToArray())}")
                    End If

                Case IrdaBeamSendState.ObexConnect
                    If IsObexResponse(data, &HA0) Then
                        AppendIrdaBeamLog("IrDA beam OBEX connected; sending file metadata.")
                        irdaBeamState = IrdaBeamSendState.ObexPutMeta
                        SendIrdaBeamPutMetadata()
                    Else
                        AppendIrdaBeamLog($"IrDA beam unexpected OBEX connect response: {FormatBytes(data.Take(Math.Min(data.Length, 16)).ToArray())}")
                    End If

                Case IrdaBeamSendState.ObexPutMeta
                    If IsObexResponse(data, &H90) OrElse IsObexResponse(data, &HA0) Then
                        AppendIrdaBeamLog("IrDA beam metadata accepted; sending file body.")
                        irdaBeamState = IrdaBeamSendState.ObexPutBody
                        SendIrdaBeamNextBodyChunk()
                    Else
                        AppendIrdaBeamLog($"IrDA beam unexpected metadata response: {FormatBytes(data.Take(Math.Min(data.Length, 16)).ToArray())}")
                    End If

                Case IrdaBeamSendState.ObexPutBody
                    If IsObexResponse(data, &H90) Then
                        SendIrdaBeamNextBodyChunk()
                    ElseIf IsObexResponse(data, &HA0) Then
                        AppendIrdaBeamLog("IrDA beam body accepted; disconnecting.")
                        irdaBeamState = IrdaBeamSendState.ObexDisconnect
                        SendIrdaBeamInformation(WrapTinyTpObex(New Byte() {&H81, &H0, &H3}), "OBEX-DISCONNECT")
                    Else
                        AppendIrdaBeamLog($"IrDA beam unexpected body response: {FormatBytes(data.Take(Math.Min(data.Length, 16)).ToArray())}")
                    End If

                Case IrdaBeamSendState.ObexDisconnect
                    If IsObexResponse(data, &HA0) Then
                        AppendIrdaBeamLog("IrDA beam OBEX disconnected.")
                        irdaBeamState = IrdaBeamSendState.LinkDisconnect
                        SendIrdaBeamControl(IrdaDiscControl, "BEAM-DISC")
                    Else
                        AppendIrdaBeamLog($"IrDA beam unexpected disconnect response: {FormatBytes(data.Take(Math.Min(data.Length, 16)).ToArray())}")
                    End If
            End Select
        End Sub

        Private Sub SendIrdaBeamDiscovery()
            Dim slot = irdaBeamDiscoverySlot
            If slot > 5 Then slot = -1
            irdaBeamDiscoverySlot = slot + 1

            Dim payload(13) As Byte
            payload(0) = IrdaBroadcastAddress
            payload(1) = IrdaUiControl
            payload(2) = IrdaXidFormat
            WriteU32(payload, 3, IrdaHostAddress)
            WriteU32(payload, 7, &HFFFFFFFFUI)
            payload(11) = 0
            payload(12) = If(slot < 0, CByte(&HFF), CByte(slot))
            payload(13) = 0
            SendIrdaBeamPayload(payload, $"BEAM-XID slot=${payload(12):X2}")
        End Sub

        Private Sub SendIrdaBeamSnrm()
            Dim parameters = New Byte() {
                &H1, &H1, &H2,
                &H82, &H1, &H1,
                &H83, &H1, &H1,
                &H84, &H1, &H1,
                &H85, &H1, &H8,
                &H86, &H1, &H7,
                &H8, &H1, &HFF
            }
            Dim payload(11 + parameters.Length - 1) As Byte
            payload(0) = IrdaBroadcastAddress
            payload(1) = IrdaSnrmControl
            WriteU32(payload, 2, IrdaHostAddress)
            WriteU32(payload, 6, irdaBeamPeerAddress)
            payload(10) = irdaBeamConnectionAddress
            Array.Copy(parameters, 0, payload, 11, parameters.Length)
            SendIrdaBeamPayload(payload, "BEAM-SNRM")
        End Sub

        Private Sub SendIrdaBeamInformation(data As Byte(), label As String)
            Dim control = CByte(IrdaFinalBit Or ((irdaBeamTransmitNext And &H7) << 1) Or ((irdaBeamReceiveNext And &H7) << 5))
            Dim payload(2 + data.Length - 1) As Byte
            payload(0) = CByte(irdaBeamConnectionAddress Or 1)
            payload(1) = control
            Array.Copy(data, 0, payload, 2, data.Length)
            irdaBeamTransmitNext = CByte((irdaBeamTransmitNext + 1) And &H7)
            SendIrdaBeamPayload(payload, $"{label} ctrl=${control:X2}")
        End Sub

        Private Sub SendIrdaBeamControl(control As Byte, label As String)
            Dim payload = New Byte() {CByte(irdaBeamConnectionAddress Or 1), control}
            SendIrdaBeamPayload(payload, label)
            If control = IrdaDiscControl Then
                AppendIrdaBeamLog($"IrDA beam send complete: {irdaBeamFileName}")
                ResetIrdaBeamSender()
            End If
        End Sub

        Private Sub SendIrdaBeamPayload(payload As Byte(), label As String)
            Dim frame = BuildIrdaSirFrame(payload)
            Dim written = NativeMusashi.palm_native_uart_write_rx(frame, CUInt(frame.Length))
            irdaBeamLastProgressTick = Environment.TickCount64
            AppendIrdaTrace($"BEAM TX {label} wrote={written} {FormatBytes(frame)}")
            AppendIrdaBeamLog($"IrDA beam TX {label} payload={FormatBytes(payload.Take(Math.Min(payload.Length, 24)).ToArray())}")
            If label.StartsWith("BEAM-XID", StringComparison.Ordinal) AndAlso VerboseSerialLog Then Append($"IrDA {label}")
        End Sub

        Private Sub SendIrdaBeamPutMetadata()
            Dim headers As New List(Of Byte)
            headers.AddRange(BuildObexNameHeader(irdaBeamFileName))
            Dim objectType = IrdaBeamObexTypeForFile(irdaBeamFileName)
            If objectType.Length > 0 Then headers.AddRange(BuildObexTypeHeader(objectType))
            headers.AddRange(BuildObexLengthHeader(If(irdaBeamFileBytes Is Nothing, 0, irdaBeamFileBytes.Length)))
            If IsIrdaBeamTextFile() Then
                headers.AddRange(BuildObexUnicodeHeader(&H5, Path.GetFileNameWithoutExtension(irdaBeamFileName)))
                headers.AddRange(BuildObexU32Header(&HCF, &H6D656D6FUI)) ' "memo" Exchange Manager target.
            End If
            Dim packet = BuildObexPacket(&H2, headers.ToArray())
            SendIrdaBeamInformation(WrapTinyTpObex(packet), If(objectType.Length = 0, "OBEX-PUT-META", $"OBEX-PUT-META type={objectType}"))
        End Sub

        Private Sub SendIrdaBeamNextBodyChunk(Optional includeMetadata As Boolean = False)
            If irdaBeamFileBytes Is Nothing Then Return

            Dim remaining = irdaBeamFileBytes.Length - irdaBeamOffset
            If remaining <= 0 Then
                If IsIrdaBeamTextFile() AndAlso Not irdaBeamEmptyEndBodySent Then
                    irdaBeamEmptyEndBodySent = True
                    Dim finalPacket = BuildObexPacket(&H82, New Byte() {&H49, &H0, &H3})
                    SendIrdaBeamInformation(WrapTinyTpObex(finalPacket), "OBEX-PUT-FINAL-EMPTY")
                Else
                    irdaBeamState = IrdaBeamSendState.ObexDisconnect
                    SendIrdaBeamInformation(WrapTinyTpObex(New Byte() {&H81, &H0, &H3}), "OBEX-DISCONNECT")
                End If
                Return
            End If

            Dim count = Math.Min(IrdaBeamBodyChunkSize, remaining)
            Dim isFinal = irdaBeamOffset + count >= irdaBeamFileBytes.Length AndAlso Not IsIrdaBeamTextFile()
            Dim chunk(count - 1) As Byte
            Array.Copy(irdaBeamFileBytes, irdaBeamOffset, chunk, 0, count)
            irdaBeamOffset += count

            Dim headers As New List(Of Byte)
            If includeMetadata Then
                headers.AddRange(BuildObexNameHeader(irdaBeamFileName))
                headers.AddRange(BuildObexLengthHeader(irdaBeamFileBytes.Length))
            End If
            headers.Add(If(isFinal, CByte(&H49), CByte(&H48)))
            headers.Add(CByte(((count + 3) >> 8) And &HFF))
            headers.Add(CByte((count + 3) And &HFF))
            headers.AddRange(chunk)
            Dim packet = BuildObexPacket(If(isFinal, CByte(&H82), CByte(&H2)), headers.ToArray())
            SendIrdaBeamInformation(WrapTinyTpObex(packet), $"OBEX-PUT-BODY {irdaBeamOffset}/{irdaBeamFileBytes.Length}")
        End Sub

        Private Sub ResetIrdaBeamSender()
            irdaBeamState = IrdaBeamSendState.Idle
            irdaBeamPeerAddress = 0
            irdaBeamConnectionAddress = 0
            irdaBeamReceiveNext = 0
            irdaBeamTransmitNext = 0
            irdaBeamFileName = ""
            irdaBeamFileBytes = Nothing
            irdaBeamOffset = 0
            irdaBeamLastProgressTick = 0
            irdaBeamDiscoverySlot = -1
            irdaBeamEmptyEndBodySent = False
        End Sub

        Private Shared Function BuildLmpConnectPayload(destinationLsap As Byte, sourceLsap As Byte, initialCredit As Byte) As Byte()
            If initialCredit = 0 Then
                Return New Byte() {CByte(destinationLsap Or IrdaControlBit), sourceLsap, IrdaLmpConnectCommand, 0}
            End If
            Return New Byte() {CByte(destinationLsap Or IrdaControlBit), sourceLsap, IrdaLmpConnectCommand, 0, initialCredit}
        End Function

        Private Shared Function BuildIasObexQueryPayload() As Byte()
            Dim className = Encoding.ASCII.GetBytes("IrDA:IrCOMM")
            Dim attributeName = Encoding.ASCII.GetBytes("IrDA:TinyTP:LsapSel")
            Dim payload As New List(Of Byte) From {&H0, &H1, CByte(IrdaIasGetValueByClass Or IrdaIasLast), 0}
            payload.Add(CByte(className.Length))
            payload.AddRange(className)
            payload.Add(CByte(attributeName.Length))
            payload.AddRange(attributeName)
            Return payload.ToArray()
        End Function

        Private Shared Function WrapTinyTpObex(obexPacket As Byte()) As Byte()
            Dim payload(3 + obexPacket.Length - 1) As Byte
            payload(0) = IrdaObexLsap
            payload(1) = 3
            payload(2) = &H10
            Array.Copy(obexPacket, 0, payload, 3, obexPacket.Length)
            Return payload
        End Function

        Private Shared Function BuildObexConnectPacket() As Byte()
            Return New Byte() {&H80, &H0, &H7, &H10, &H0, &H4, &H0}
        End Function

        Private Shared Function BuildObexPacket(opcode As Byte, headers As Byte()) As Byte()
            Dim packet(3 + headers.Length - 1) As Byte
            packet(0) = opcode
            WriteU16(packet, 1, CUShort(packet.Length))
            Array.Copy(headers, 0, packet, 3, headers.Length)
            Return packet
        End Function

        Private Shared Function BuildObexLengthHeader(length As Integer) As Byte()
            Dim header(4) As Byte
            header(0) = &HC3
            WriteU32(header, 1, CUInt(Math.Max(0, length)))
            Return header
        End Function

        Private Shared Function BuildObexTypeHeader(mimeType As String) As Byte()
            Dim typeBytes = Encoding.ASCII.GetBytes(If(mimeType, ""))
            Dim header(3 + typeBytes.Length) As Byte
            header(0) = &H42
            WriteU16(header, 1, CUShort(header.Length))
            Array.Copy(typeBytes, 0, header, 3, typeBytes.Length)
            header(header.Length - 1) = 0
            Return header
        End Function

        Private Shared Function BuildObexU32Header(headerId As Byte, value As UInteger) As Byte()
            Dim header(4) As Byte
            header(0) = headerId
            WriteU32(header, 1, value)
            Return header
        End Function

        Private Shared Function BuildObexUnicodeHeader(headerId As Byte, value As String) As Byte()
            Dim encoded As New List(Of Byte)
            For Each ch In If(value, "").ToCharArray()
                encoded.Add(CByte((AscW(ch) >> 8) And &HFF))
                encoded.Add(CByte(AscW(ch) And &HFF))
            Next
            encoded.Add(0)
            encoded.Add(0)

            Dim header(3 + encoded.Count - 1) As Byte
            header(0) = headerId
            WriteU16(header, 1, CUShort(header.Length))
            encoded.CopyTo(header, 3)
            Return header
        End Function

        Private Shared Function BuildObexNameHeader(name As String) As Byte()
            Return BuildObexUnicodeHeader(&H1, name)
        End Function

        Private Shared Function IsObexResponse(data As Byte(), responseCode As Byte) As Boolean
            If data.Length < 6 Then Return False
            Dim cursor = 0
            If data.Length >= 3 AndAlso ((data(0) And &H7F) = IrdaObexLsap OrElse (data(1) And &H7F) = IrdaObexLsap) Then cursor = 3
            Return data.Length > cursor AndAlso data(cursor) = responseCode
        End Function

        Private Shared Function IrdaBeamObexTypeForFile(fileName As String) As String
            Select Case Path.GetExtension(If(fileName, "")).ToLowerInvariant()
                Case ".txt"
                    Return ""
                Case Else
                    Return ""
            End Select
        End Function

        Private Function IsIrdaBeamTextFile() As Boolean
            Return Path.GetExtension(If(irdaBeamFileName, "")).Equals(".txt", StringComparison.OrdinalIgnoreCase)
        End Function

        Private Shared Function NormalizeIrdaBeamTextBytes(bytes As Byte()) As Byte()
            Dim text = Encoding.Default.GetString(If(bytes, Array.Empty(Of Byte)()))
            text = text.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Replace(vbLf, vbCrLf)
            Return Encoding.Default.GetBytes(text)
        End Function

        Private Sub HandleIrdaFrame(frame As Byte())
            If Not IrdaFrameHasValidFcs(frame) Then Return
            If irdaBeamState <> IrdaBeamSendState.Idle AndAlso HandleIrdaBeamSenderFrame(frame) Then Return

            If frame.Length >= 16 AndAlso frame(0) = IrdaBroadcastAddress AndAlso frame(1) = IrdaUiControl AndAlso frame(2) = IrdaXidFormat Then
                HandleIrdaXidFrame(frame)
                Return
            End If

            If frame.Length >= 13 AndAlso frame(1) = IrdaSnrmControl Then
                HandleIrdaSnrmFrame(frame)
                Return
            End If

            If frame.Length >= 4 AndAlso irdaConnectionAddress <> 0 AndAlso
                (frame(0) = irdaConnectionAddress OrElse frame(0) = (irdaConnectionAddress Or 1)) Then
                HandleIrdaConnectedFrame(frame)
            End If
        End Sub

        Private Sub HandleIrdaXidFrame(frame As Byte())
            Dim sourceAddress = ReadU32(frame, 3)
            Dim slot = frame(12)

            If sourceAddress <> irdaLastDiscoverySource Then
                irdaLastDiscoverySource = sourceAddress
                irdaRespondedInDiscovery = False
            End If

            If slot = &HFF Then
                irdaRespondedInDiscovery = False
                Return
            End If

            If irdaRespondedInDiscovery Then Return
            If slot <> 2 Then Return

            SendIrdaXidResponse(sourceAddress, slot)
            irdaRespondedInDiscovery = True
        End Sub

        Private Sub HandleIrdaSnrmFrame(frame As Byte())
            Dim sourceAddress = ReadU32(frame, 2)
            Dim destinationAddress = ReadU32(frame, 6)
            If destinationAddress <> IrdaHostAddress Then Return

            Dim connectionAddress = frame(10)
            irdaConnectionAddress = CByte(connectionAddress And &HFE)
            irdaConnectedPeer = sourceAddress
            irdaReceiveNext = 0
            irdaTransmitNext = 0
            irdaLastInformationResponse = Nothing
            irdaLastInformationResponseLabel = ""
            Dim parameterCount = Math.Max(0, frame.Length - 13)
            Dim parameters As Byte() = Array.Empty(Of Byte)()
            If parameterCount > 0 Then ReDim parameters(parameterCount - 1)
            If parameterCount > 0 Then Array.Copy(frame, 11, parameters, 0, parameterCount)

            SendIrdaUaResponse(sourceAddress, connectionAddress, NegotiateIrdaParameters(parameters))
        End Sub

        Private Sub HandleIrdaConnectedFrame(frame As Byte())
            Dim control = frame(1)
            irdaConnectedResponseAddress = frame(0)
            AppendIrdaTrace($"RX connected addr=${frame(0):X2} ctrl=${control:X2} len={frame.Length} {FormatBytes(frame)}")

            If IsIrdaInformationControl(control) Then
                HandleIrdaInformationFrame(frame)
                Return
            End If

            If IsIrdaReceiveReadyControl(control) Then
                SendIrdaReceiveReady("RR")
            ElseIf control = IrdaDiscControl Then
                SendIrdaConnectedControl(IrdaUaResponseControl, "UA-DISC")
                ResetIrdaConnectionState()
            End If
        End Sub

        Private Sub HandleIrdaInformationFrame(frame As Byte())
            If frame.Length < 6 Then Return

            Dim control = frame(1)
            Dim sendSequence = CByte((control >> 1) And &H7)
            If sendSequence = irdaReceiveNext Then
                irdaReceiveNext = CByte((irdaReceiveNext + 1) And &H7)
                Dim dataLength = frame.Length - 4
                Dim data(dataLength - 1) As Byte
                Array.Copy(frame, 2, data, 0, dataLength)
                If HandleIrdaInformationPayload(data) Then Return
            Else
                AppendIrdaTrace($"RX duplicate/out-of-order I ns={sendSequence} expected={irdaReceiveNext}")
                If irdaLastInformationResponse IsNot Nothing Then
                    QueueIrdaResponse(irdaLastInformationResponse, irdaLastInformationResponseLabel, IrdaConnectedTurnaroundDelayMs)
                    Return
                End If
            End If

            SendIrdaReceiveReady("RR")
        End Sub

        Private Function HandleIrdaInformationPayload(data As Byte()) As Boolean
            AppendIrdaTrace($"RX I payload {FormatBytes(data)}")
            If data.Length >= 4 AndAlso (data(0) And IrdaControlBit) <> 0 AndAlso data(2) = IrdaLmpConnectCommand Then
                Dim destinationLsap = CByte(data(0) And &H7F)
                Dim sourceLsap = CByte(data(1) And &H7F)
                Dim initialCredit As Byte = If(destinationLsap = IrdaObexLsap, CByte(&H10), CByte(0))
                Dim responseLength = If(initialCredit = 0, 4, 5)
                Dim response(responseLength - 1) As Byte
                response(0) = CByte(sourceLsap Or IrdaControlBit)
                response(1) = destinationLsap
                response(2) = IrdaLmpConnectConfirm
                response(3) = 0
                If responseLength > 4 Then response(4) = initialCredit
                SendIrdaInformationResponse(response, $"LMP-CONNECT-CNF dlsap=${sourceLsap:X2} slsap=${destinationLsap:X2}")
                Return True
            End If
            If IsIrdaIasGetValueByClass(data) Then
                SendIrdaIasGetValueByClassResponse(data)
                Return True
            End If
            If HandleIrdaTinyTpObexPayload(data) Then Return True
            Return False
        End Function

        Private Function HandleIrdaTinyTpObexPayload(data As Byte()) As Boolean
            If data.Length < 3 Then Return False

            Dim destinationLsap = CByte(data(0) And &H7F)
            Dim sourceLsap = CByte(data(1) And &H7F)
            If destinationLsap <> IrdaObexLsap Then Return False

            Dim obexOffset = 3
            If irdaObexExpectedLength > 0 Then
                If data.Length <= obexOffset Then Return False
                AppendObexFragment(data, obexOffset, data.Length - obexOffset)
                If irdaObexPacket.Count < irdaObexExpectedLength Then
                    AppendIrdaTrace($"OBEX fragment {irdaObexPacket.Count}/{irdaObexExpectedLength}")
                    Return False
                End If

                Dim packet = irdaObexPacket.Take(irdaObexExpectedLength).ToArray()
                irdaObexPacket.Clear()
                irdaObexExpectedLength = 0
                Return HandleCompleteObexPacket(packet, irdaObexClientLsap)
            End If

            If data.Length - obexOffset < 3 Then Return False

            Dim opcode = data(obexOffset)
            Dim packetLength = (CInt(data(obexOffset + 1)) << 8) Or data(obexOffset + 2)
            If packetLength < 3 Then
                AppendIrdaTrace($"OBEX malformed opcode=${opcode:X2} len={packetLength} payload={FormatBytes(data)}")
                Return False
            End If

            If obexOffset + packetLength > data.Length Then
                irdaObexExpectedLength = packetLength
                irdaObexClientLsap = sourceLsap
                irdaObexPacket.Clear()
                AppendObexFragment(data, obexOffset, data.Length - obexOffset)
                AppendIrdaTrace($"OBEX start opcode=${opcode:X2} {irdaObexPacket.Count}/{irdaObexExpectedLength}")
                Return False
            End If

            Dim completePacket(packetLength - 1) As Byte
            Array.Copy(data, obexOffset, completePacket, 0, packetLength)
            Return HandleCompleteObexPacket(completePacket, sourceLsap)
        End Function

        Private Sub AppendObexFragment(data As Byte(), offset As Integer, count As Integer)
            For i = 0 To count - 1
                irdaObexPacket.Add(data(offset + i))
            Next
        End Sub

        Private Function HandleCompleteObexPacket(packet As Byte(), clientLsap As Byte) As Boolean
            If packet.Length < 3 Then Return False

            Dim opcode = packet(0)
            Dim packetLength = (CInt(packet(1)) << 8) Or packet(2)
            AppendIrdaTrace($"OBEX packet opcode=${opcode:X2} len={packetLength} body={irdaObexBody.Count} name='{irdaObexObjectName}'")
            If packetLength < 3 OrElse packetLength > packet.Length Then
                AppendIrdaTrace($"OBEX malformed opcode=${opcode:X2} len={packetLength} packet={FormatBytes(packet)}")
                Return False
            End If

            Select Case opcode
                Case &H80
                    irdaObexObjectName = ""
                    irdaObexObjectType = ""
                    irdaObexBody.Clear()
                    Dim response = New Byte() {
                        clientLsap,
                        IrdaObexLsap,
                        &H10,
                        &HA0, &H00, &H07,
                        &H10, &H00, &H04, &H00
                    }
                    SendIrdaInformationResponse(response, "OBEX CONNECT OK")
                    Return True
                Case &H81
                    ' OBEX DISCONNECT also expects a success response; without it Palm OS
                    ' remains on the beam dialog's "Disconnecting" state after saving.
                    AppendIrdaTrace($"OBEX disconnect requested name='{irdaObexObjectName}' body={irdaObexBody.Count}")
                    SendIrdaObexResponse(clientLsap, &HA0, "OBEX DISCONNECT OK")
                    irdaObexObjectName = ""
                    irdaObexObjectType = ""
                    irdaObexBody.Clear()
                    irdaObexPacket.Clear()
                    irdaObexExpectedLength = 0
                    Return True
                Case &H2, &H82
                    AppendIrdaBeamLog($"IrDA receive OBEX PUT opcode=${opcode:X2} len={packetLength} head={FormatBytes(packet.Take(Math.Min(packetLength, 96)).ToArray())}")
                    HandleObexHeaders(packet, 3, packetLength)
                    If opcode = &H82 Then
                        AppendIrdaTrace($"OBEX final PUT name='{irdaObexObjectName}' body={irdaObexBody.Count}")
                        SaveIrdaObexObject()
                        SendIrdaObexResponse(clientLsap, &HA0, "OBEX PUT OK")
                    Else
                        AppendIrdaTrace($"OBEX continue PUT name='{irdaObexObjectName}' body={irdaObexBody.Count}")
                        SendIrdaObexResponse(clientLsap, &H90, "OBEX PUT CONTINUE")
                    End If
                    Return True
                Case Else
                    AppendIrdaTrace($"OBEX opcode=${opcode:X2} len={packetLength} packet={FormatBytes(packet)}")
                    Return False
            End Select
        End Function

        Private Sub SendIrdaObexResponse(clientLsap As Byte, responseCode As Byte, label As String)
            Dim response = New Byte() {
                clientLsap,
                IrdaObexLsap,
                &H10,
                responseCode, &H00, &H03
            }
            SendIrdaInformationResponse(response, label)
        End Sub

        Private Sub HandleObexHeaders(data As Byte(), startOffset As Integer, endOffset As Integer)
            Dim cursor = startOffset
            While cursor < endOffset
                Dim headerId = data(cursor)
                cursor += 1

                Select Case headerId And &HC0
                    Case &H0, &H40
                        If cursor + 2 > endOffset Then Exit While
                        Dim headerLength = (CInt(data(cursor)) << 8) Or data(cursor + 1)
                        cursor += 2
                        Dim valueLength = headerLength - 3
                        If valueLength < 0 OrElse cursor + valueLength > endOffset Then Exit While

                        If headerId = &H1 Then
                            irdaObexObjectName = DecodeObexUnicode(data, cursor, valueLength)
                            AppendIrdaTrace($"OBEX name '{irdaObexObjectName}'")
                            AppendIrdaBeamLog($"IrDA receive OBEX name: '{irdaObexObjectName}'")
                        ElseIf headerId = &H42 Then
                            irdaObexObjectType = DecodeObexAscii(data, cursor, valueLength)
                            AppendIrdaTrace($"OBEX type '{irdaObexObjectType}'")
                            AppendIrdaBeamLog($"IrDA receive OBEX type: '{irdaObexObjectType}'")
                        ElseIf headerId = &H48 OrElse headerId = &H49 Then
                            For i = 0 To valueLength - 1
                                irdaObexBody.Add(data(cursor + i))
                            Next
                            AppendIrdaTrace($"OBEX body += {valueLength} byte(s), total {irdaObexBody.Count}")
                        Else
                            AppendIrdaBeamLog($"IrDA receive OBEX header ${headerId:X2} len={headerLength} value={FormatBytes(data.Skip(cursor).Take(Math.Min(valueLength, 32)).ToArray())}")
                        End If

                        cursor += valueLength
                    Case &H80
                        If cursor < endOffset Then AppendIrdaBeamLog($"IrDA receive OBEX header ${headerId:X2} byte=${data(cursor):X2}")
                        cursor += 1
                    Case &HC0
                        If headerId = &HC3 AndAlso cursor + 4 <= endOffset Then
                            AppendIrdaBeamLog($"IrDA receive OBEX length: {ReadU32(data, cursor)}")
                        ElseIf cursor + 4 <= endOffset Then
                            AppendIrdaBeamLog($"IrDA receive OBEX header ${headerId:X2} value={ReadU32(data, cursor)}")
                        End If
                        cursor += 4
                End Select
            End While
        End Sub

        Private Sub SaveIrdaObexObject()
            Directory.CreateDirectory(irdaCapturePath)

            Dim fileName = If(String.IsNullOrWhiteSpace(irdaObexObjectName), $"beam_{DateTime.Now:yyyyMMdd_HHmmss}.bin", irdaObexObjectName)
            fileName = SanitizeBmpFileName(fileName)
            Dim outputPath As String = UniquePath(Path.Combine(irdaCapturePath, fileName))

            File.WriteAllBytes(outputPath, irdaObexBody.ToArray())
            Append($"IrDA received: {Path.GetFileName(outputPath)} ({irdaObexBody.Count} bytes)")
            AppendIrdaTrace($"OBEX saved {irdaObexBody.Count} byte(s) -> {outputPath}")
        End Sub

        Private Shared Function UniquePath(filePath As String) As String
            If Not File.Exists(filePath) Then Return filePath

            Dim directory = Path.GetDirectoryName(filePath)
            Dim baseName = Path.GetFileNameWithoutExtension(filePath)
            Dim extension = Path.GetExtension(filePath)
            Dim suffix = DateTime.Now.ToString("yyyyMMdd_HHmmss")
            Dim candidate = Path.Combine(directory, $"{baseName}_{suffix}{extension}")
            If Not File.Exists(candidate) Then Return candidate

            Dim index = 2
            Do
                candidate = Path.Combine(directory, $"{baseName}_{suffix}_{index:D2}{extension}")
                If Not File.Exists(candidate) Then Return candidate
                index += 1
            Loop
        End Function

        Private Shared Function DecodeObexUnicode(data As Byte(), offset As Integer, count As Integer) As String
            Dim builder As New StringBuilder()
            Dim endOffset = offset + count
            Dim cursor = offset
            While cursor + 1 < endOffset
                Dim code = (CInt(data(cursor)) << 8) Or data(cursor + 1)
                If code = 0 Then Exit While
                builder.Append(ChrW(code))
                cursor += 2
            End While
            Return builder.ToString()
        End Function

        Private Shared Function DecodeObexAscii(data As Byte(), offset As Integer, count As Integer) As String
            Dim endOffset = offset + count
            If count > 0 AndAlso offset + count <= data.Length AndAlso data(offset + count - 1) = 0 Then endOffset -= 1
            If endOffset <= offset Then Return ""
            Return Encoding.ASCII.GetString(data, offset, endOffset - offset)
        End Function

        Private Shared Function IsIrdaIasGetValueByClass(data As Byte()) As Boolean
            If data.Length < 5 Then Return False
            Return (data(0) And &H7F) = 0 AndAlso (data(2) And &H7F) = IrdaIasGetValueByClass
        End Function

        Private Sub SendIrdaIasGetValueByClassResponse(request As Byte())
            Dim className = ""
            Dim attributeName = ""
            Dim cursor = 3
            If cursor < request.Length Then
                Dim classLength = request(cursor)
                cursor += 1
                If cursor + classLength <= request.Length Then
                    className = Encoding.ASCII.GetString(request, cursor, classLength)
                    cursor += classLength
                End If
            End If
            If cursor < request.Length Then
                Dim attributeLength = request(cursor)
                cursor += 1
                If cursor + attributeLength <= request.Length Then
                    attributeName = Encoding.ASCII.GetString(request, cursor, attributeLength)
                End If
            End If

            Dim clientLsap = CByte(request(1) And &H7F)
            Dim response(12) As Byte
            response(0) = clientLsap
            response(1) = 0
            response(2) = CByte(IrdaIasGetValueByClass Or IrdaIasLast)
            response(3) = IrdaIasSuccess
            response(4) = 0
            response(5) = 1
            response(6) = 0
            response(7) = 0
            response(8) = IrdaIasInteger
            response(9) = 0
            response(10) = 0
            response(11) = 0
            response(12) = IrdaObexLsap

            SendIrdaInformationResponse(response, $"IAS {className}/{attributeName} LSAP=${IrdaObexLsap:X2}")
        End Sub

        Private Sub SendIrdaXidResponse(destinationAddress As UInteger, slot As Byte)
            Dim nameBytes = Encoding.ASCII.GetBytes("ESP32-PALM")
            Dim payload(13 + 4 + nameBytes.Length - 1) As Byte
            payload(0) = IrdaDiscoveryResponseAddress
            payload(1) = IrdaXidResponseControl
            payload(2) = IrdaXidFormat
            WriteU32(payload, 3, IrdaHostAddress)
            WriteU32(payload, 7, destinationAddress)
            payload(11) = 0
            payload(12) = slot
            payload(13) = 0
            payload(14) = &H82
            payload(15) = &H20
            payload(16) = 0
            Array.Copy(nameBytes, 0, payload, 17, nameBytes.Length)

            pendingIrdaResponse = BuildIrdaSirFrame(payload)
            pendingIrdaResponseLabel = $"XID slot=${slot:X2} peer=${destinationAddress:X8}"
            pendingIrdaResponseDueTick = Environment.TickCount64 + IrdaTurnaroundDelayMs
            AppendIrdaTrace($"QUEUE {pendingIrdaResponseLabel} {FormatBytes(pendingIrdaResponse)}")
        End Sub

        Private Sub SendIrdaUaResponse(destinationAddress As UInteger, connectionAddress As Byte, parameters As Byte())
            Dim payload(10 + parameters.Length - 1) As Byte
            payload(0) = connectionAddress
            payload(1) = IrdaUaResponseControl
            WriteU32(payload, 2, IrdaHostAddress)
            WriteU32(payload, 6, destinationAddress)
            If parameters.Length > 0 Then Array.Copy(parameters, 0, payload, 10, parameters.Length)

            pendingIrdaResponse = BuildIrdaSirFrame(payload)
            pendingIrdaResponseLabel = $"UA ca=${connectionAddress:X2} peer=${destinationAddress:X8}"
            pendingIrdaResponseDueTick = Environment.TickCount64 + IrdaTurnaroundDelayMs
            AppendIrdaTrace($"QUEUE {pendingIrdaResponseLabel} {FormatBytes(pendingIrdaResponse)}")
        End Sub

        Private Shared Function NegotiateIrdaParameters(requested As Byte()) As Byte()
            If requested Is Nothing OrElse requested.Length = 0 Then Return requested

            Dim negotiated As Byte() = CType(requested.Clone(), Byte())
            Dim index = 0
            While index + 2 < negotiated.Length
                Dim pi = negotiated(index)
                Dim length = negotiated(index + 1)
                Dim valueIndex = index + 2
                If valueIndex + length > negotiated.Length Then Exit While

                Select Case pi
                    Case &H1
                        ' Keep the link at 9600 bps for now; higher IrDA rates need real UART timing.
                        If length >= 1 Then negotiated(valueIndex) = CByte(negotiated(valueIndex) And &H2)
                    Case &H83
                        ' Limit received I-field size to the smallest offered size.
                        If length >= 1 Then negotiated(valueIndex) = CByte(negotiated(valueIndex) And &H1)
                    Case &H84
                        ' Window size 1 keeps primary/secondary turn-taking simple.
                        If length >= 1 Then negotiated(valueIndex) = CByte(negotiated(valueIndex) And &H1)
                End Select

                index += 2 + length
            End While

            Return negotiated
        End Function

        Private Sub SendIrdaConnectedControl(control As Byte, label As String)
            If irdaConnectionAddress = 0 Then Return

            Dim responseAddress = irdaConnectionAddress
            Dim payload = New Byte() {responseAddress, control}
            Dim frame = BuildIrdaSirFrame(payload)
            Dim responseLabel = $"{label} ca=${irdaConnectionAddress:X2} addr=${responseAddress:X2}"
            QueueIrdaResponse(frame, responseLabel, IrdaConnectedTurnaroundDelayMs)
        End Sub

        Private Sub SendIrdaReceiveReady(label As String)
            Dim control = CByte(IrdaRrPollFinalControl Or ((irdaReceiveNext And &H7) << 5))
            SendIrdaConnectedControl(control, label)
        End Sub

        Private Sub SendIrdaInformationResponse(data As Byte(), label As String)
            If irdaConnectionAddress = 0 Then Return

            Dim control = CByte(IrdaFinalBit Or ((irdaTransmitNext And &H7) << 1) Or ((irdaReceiveNext And &H7) << 5))
            Dim payload(2 + data.Length - 1) As Byte
            payload(0) = irdaConnectionAddress
            payload(1) = control
            Array.Copy(data, 0, payload, 2, data.Length)

            Dim frame = BuildIrdaSirFrame(payload)
            Dim responseLabel = $"{label} ca=${irdaConnectionAddress:X2} ctrl=${control:X2}"
            irdaTransmitNext = CByte((irdaTransmitNext + 1) And &H7)
            irdaLastInformationResponse = frame
            irdaLastInformationResponseLabel = responseLabel
            QueueIrdaResponse(frame, responseLabel, IrdaConnectedTurnaroundDelayMs)
        End Sub

        Private Sub ResetIrdaConnectionState()
            irdaConnectionAddress = 0
            irdaConnectedResponseAddress = 0
            irdaConnectedPeer = 0
            irdaReceiveNext = 0
            irdaTransmitNext = 0
            irdaLastInformationResponse = Nothing
            irdaLastInformationResponseLabel = ""
            irdaObexObjectName = ""
            irdaObexObjectType = ""
            irdaObexBody.Clear()
            irdaObexPacket.Clear()
            irdaObexExpectedLength = 0
            irdaObexClientLsap = 0
            irdaTxQuietSinceTick = 0
        End Sub

        Private Shared Function IsIrdaInformationControl(control As Byte) As Boolean
            Return (control And &H1) = 0
        End Function

        Private Shared Function IsIrdaReceiveReadyControl(control As Byte) As Boolean
            Return (control And &H1F) = IrdaRrPollFinalControl
        End Function

        Private Sub SendPendingIrdaResponse()
            If pendingIrdaResponse Is Nothing AndAlso pendingIrdaResponses.Count > 0 Then
                Dim queued = pendingIrdaResponses.Dequeue()
                pendingIrdaResponse = queued.Frame
                pendingIrdaResponseLabel = queued.Label
                pendingIrdaResponseDueTick = queued.Due
            End If

            If pendingIrdaResponse Is Nothing OrElse pendingIrdaResponse.Length = 0 Then Return
            If NativeMusashi.palm_native_uart_tx_count() <> 0UI Then Return
            Dim nowTick = Environment.TickCount64
            If irdaTxQuietSinceTick = 0 Then
                irdaTxQuietSinceTick = nowTick
                Return
            End If
            If nowTick - irdaTxQuietSinceTick < IrdaTxQuietBeforeResponseMs Then Return
            If nowTick < pendingIrdaResponseDueTick Then Return

            Dim response = pendingIrdaResponse
            Dim label = pendingIrdaResponseLabel
            pendingIrdaResponse = Nothing
            pendingIrdaResponseLabel = ""
            pendingIrdaResponseDueTick = 0

            Dim written = NativeMusashi.palm_native_uart_write_rx(response, CUInt(response.Length))
            AppendIrdaTrace($"TX {label} wrote={written} {FormatBytes(response)}")
            If VerboseSerialLog Then Append($"IrDA {label} response {written}")
        End Sub

        Private Sub QueueIrdaResponse(frame As Byte(), label As String, delayMs As Long)
            Dim due = Environment.TickCount64 + delayMs
            pendingIrdaResponses.Enqueue(New IrdaPendingResponse(frame, label, due))
            AppendIrdaTrace($"QUEUE {label} delayMs={delayMs} {FormatBytes(frame)}")
        End Sub

        Private Shared Function BuildIrdaSirFrame(payload As Byte()) As Byte()
            Dim bytes As New List(Of Byte)
            For i = 1 To 10
                bytes.Add(&HFF)
            Next
            bytes.Add(IrdaSirBof)

            Dim crc = Crc16X25(payload, payload.Length)
            For Each value In payload
                AddEscapedIrdaByte(bytes, value)
            Next
            AddEscapedIrdaByte(bytes, CByte(crc And &HFF))
            AddEscapedIrdaByte(bytes, CByte((crc >> 8) And &HFF))
            bytes.Add(IrdaSirEof)
            Return bytes.ToArray()
        End Function

        Private Shared Sub AddEscapedIrdaByte(bytes As List(Of Byte), value As Byte)
            If value = IrdaSirBof OrElse value = IrdaSirEof OrElse value = IrdaSirEscape Then
                bytes.Add(IrdaSirEscape)
                bytes.Add(CByte(value Xor &H20))
            Else
                bytes.Add(value)
            End If
        End Sub

        Private Shared Function IrdaFrameHasValidFcs(frame As Byte()) As Boolean
            If frame.Length < 3 Then Return False
            Dim expected = CUInt(frame(frame.Length - 2)) Or (CUInt(frame(frame.Length - 1)) << 8)
            Dim actual = CUInt(Crc16X25(frame, frame.Length - 2))
            Return expected = actual
        End Function

        Private Sub AppendIrdaTrace(message As String)
            If Not EnableIrdaTrace Then Return
            Try
                File.AppendAllText(irdaTracePath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}")
            Catch
            End Try
        End Sub

        Private Sub AppendIrdaBeamLog(message As String)
            Append(message)
            Try
                File.AppendAllText(irdaBeamLogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}")
            Catch
            End Try
        End Sub

        Private Sub ProcessPalmSerialFrames()
            Do
                While uartSlpRxBuffer.Count >= 3 AndAlso Not (uartSlpRxBuffer(0) = &HBE AndAlso uartSlpRxBuffer(1) = &HEF AndAlso uartSlpRxBuffer(2) = &HED)
                    uartSlpRxBuffer.RemoveAt(0)
                End While

                If uartSlpRxBuffer.Count < 10 Then Return

                Dim bodyLength = (CInt(uartSlpRxBuffer(6)) << 8) Or CInt(uartSlpRxBuffer(7))
                If bodyLength < 0 OrElse bodyLength > 2048 Then
                    If VerboseSerialLog Then Append($"SLP bad length {bodyLength}, resync")
                    uartSlpRxBuffer.RemoveAt(0)
                    Continue Do
                End If

                Dim frameLength = 10 + bodyLength + 2
                If uartSlpRxBuffer.Count < frameLength Then Return

                Dim frame(frameLength - 1) As Byte
                For i = 0 To frameLength - 1
                    frame(i) = uartSlpRxBuffer(i)
                Next

                Dim headerSum As Integer = 0
                For i = 0 To 8
                    headerSum = (headerSum + frame(i)) And &HFF
                Next
                If headerSum <> frame(9) Then
                    Append($"HotSync serial header checksum mismatch got ${frame(9):X2} expected ${headerSum:X2}")
                    uartSlpRxBuffer.RemoveAt(0)
                    Continue Do
                End If

                Dim expectedCrc = ReadU16(frame, frameLength - 2)
                Dim actualCrc = Crc16(frame, frameLength - 2)
                If expectedCrc <> actualCrc Then
                    Append($"HotSync serial CRC mismatch got ${expectedCrc:X4} expected ${actualCrc:X4}")
                    If VerboseSerialLog Then
                        Dim sampleLength = Math.Min(frame.Length, 96)
                        Dim sample(sampleLength - 1) As Byte
                        Array.Copy(frame, sample, sampleLength)
                        Append($"Bad SLP frame type ${frame(5):X2} tx ${frame(8):X2} body {bodyLength} total {frameLength}: {FormatBytes(sample)}")
                    End If
                    uartSlpRxBuffer.RemoveAt(0)
                    Continue Do
                End If

                uartSlpRxBuffer.RemoveRange(0, frameLength)
                HandlePalmSlpFrame(frame, bodyLength)
            Loop
        End Sub

        Private Sub HandlePalmSlpFrame(frame As Byte(), bodyLength As Integer)
            Dim dest = frame(3)
            Dim src = frame(4)
            Dim packetType = frame(5)
            Dim txId = frame(8)
            If packetType = 3 Then
                If VerboseSerialLog Then Append($"SLP LOOP ignored tx ${txId:X2} len {bodyLength}")
                Return
            End If

            If packetType <> 2 OrElse bodyLength < 4 Then
                If VerboseSerialLog Then Append($"SLP RX {src}->{dest} type {packetType} tx ${txId:X2} len {bodyLength}")
                Return
            End If

            Dim padType = frame(10)
            Dim padFlags = frame(11)
            Dim padSize = ReadU16(frame, 12)
            Dim quietWriteResource = installState = HotSyncInstallState.WritingResource AndAlso txId = installAwaitingTxId
            If VerboseSerialLog AndAlso Not quietWriteResource Then
                Append($"PADP RX type ${padType:X2} flags ${padFlags:X2} size {padSize} tx ${txId:X2}")
            End If
            If padType = &H8 Then
                If VerboseSerialLog Then Append("PADP abort from Palm.")
                pendingPadpActive = False
                pendingPadpPayload = Nothing
                ResetPadpReceive()
                Return
            End If

            If padType = &H1 Then
                SendPadpAck(txId, padFlags, padSize, quietWriteResource)
                Dim isCmpWake = (padSize = 10 AndAlso bodyLength >= 14 AndAlso frame(14) = &H1)
                If isCmpWake Then
                    cmpHandshakeStarted = False
                    dlpReadUserInfoSent = False
                    dlpReadUserInfoQueued = False
                    dlpWriteUserInfoSent = False
                    pendingPadpActive = False
                    pendingPadpPayload = Nothing
                    If installState = HotSyncInstallState.Done OrElse installState = HotSyncInstallState.Failed Then
                        installState = HotSyncInstallState.Idle
                    End If
                    installDbId = 0
                    installResourceIndex = 0
                    installAwaitingTxId = 0
                    installAppBlockDone = False
                    installSortBlockDone = False
                    ResetPadpReceive()
                End If
                If isCmpWake AndAlso Not cmpHandshakeStarted Then
                    cmpHandshakeStarted = True
                    SendCmpInit(txId)
                ElseIf Not isCmpWake Then
                    HandlePadpDataFragment(txId, padFlags, padSize, frame, bodyLength)
                End If
            ElseIf padType = &H2 Then
                If pendingPadpActive AndAlso txId = pendingPadpTxId Then
                    SendNextPadpFragment()
                    Return
                End If
                If cmpHandshakeStarted AndAlso txId = cmpInitTxId AndAlso Not dlpReadUserInfoSent Then
                    QueueDlpReadUserInfo()
                End If
            End If
        End Sub

        Private Sub HandlePadpDataFragment(txId As Byte, padFlags As Byte, padSize As Integer, frame As Byte(), bodyLength As Integer)
            Dim chunkLength = Math.Max(0, bodyLength - 4)
            Dim isFirst = (padFlags And &H80) <> 0
            Dim isLast = (padFlags And &H40) <> 0

            If isFirst Then
                rxPadpPayload.Clear()
                rxPadpActive = True
                rxPadpTxId = txId
                rxPadpExpectedSize = padSize
            ElseIf Not rxPadpActive OrElse txId <> rxPadpTxId Then
                ResetPadpReceive()
                Return
            End If

            If rxPadpPayload.Count <> padSize AndAlso Not isFirst Then
                If VerboseSerialLog Then Append($"PADP RX offset mismatch got {padSize} expected {rxPadpPayload.Count}")
                ResetPadpReceive()
                Return
            End If

            If chunkLength > 0 Then
                For i = 0 To chunkLength - 1
                    rxPadpPayload.Add(frame(14 + i))
                Next
            End If

            If isLast Then
                If rxPadpExpectedSize > 0 AndAlso rxPadpPayload.Count > rxPadpExpectedSize Then
                    rxPadpPayload.RemoveRange(rxPadpExpectedSize, rxPadpPayload.Count - rxPadpExpectedSize)
                End If
                Dim payload = rxPadpPayload.ToArray()
                ResetPadpReceive()
                If payload.Length > 0 Then HandleDlpPayload(txId, payload)
            End If
        End Sub

        Private Sub ResetPadpReceive()
            rxPadpActive = False
            rxPadpTxId = 0
            rxPadpExpectedSize = 0
            rxPadpPayload.Clear()
        End Sub

        Private Sub QueueDlpReadUserInfo()
            dlpReadUserInfoQueued = True
            dlpReadUserInfoDueTick = Environment.TickCount64 + 150
            If VerboseSerialLog Then Append($"DLP ReadUserInfo queued tx ${hostPadpTxId:X2}")
        End Sub

        Private Sub ServiceHotSyncHost()
            If Not dlpReadUserInfoQueued OrElse dlpReadUserInfoSent Then Return
            If Environment.TickCount64 < dlpReadUserInfoDueTick Then Return
            dlpReadUserInfoQueued = False
            SendDlpReadUserInfo()
        End Sub

        Private Sub SendPadpAck(txId As Byte, padFlags As Byte, padSize As Integer, Optional quiet As Boolean = False)
            Dim body = New Byte() {&H2, padFlags, CByte((padSize >> 8) And &HFF), CByte(padSize And &HFF)}
            Dim frame = BuildSlpFrame(3, 3, 2, txId, body)
            Dim written = NativeMusashi.palm_native_uart_write_rx(frame, CUInt(frame.Length))
            If VerboseSerialLog AndAlso Not quiet Then Append($"UART RX ACK {written}: {FormatBytes(frame)}")
        End Sub

        Private Sub SendCmpInit(requestTxId As Byte)
            Dim responseTxId = CByte(If(requestTxId >= &HFE, 2, requestTxId + 2))
            cmpInitTxId = responseTxId
            hostPadpTxId = NextPadpTxId(responseTxId)
            Dim cmpInit = New Byte() {
                &H2, &H10,
                &H1, &H2,
                &H0, &H0,
                &H0, &H0, &H25, &H80
            }
            Dim padBody(4 + cmpInit.Length - 1) As Byte
            padBody(0) = &H1
            padBody(1) = &HC0
            padBody(2) = &H0
            padBody(3) = CByte(cmpInit.Length)
            Array.Copy(cmpInit, 0, padBody, 4, cmpInit.Length)

            Dim frame = BuildSlpFrame(3, 3, 2, responseTxId, padBody)
            Dim written = NativeMusashi.palm_native_uart_write_rx(frame, CUInt(frame.Length))
            If VerboseSerialLog Then Append($"UART RX CMP INIT {written}: {FormatBytes(frame)}")
        End Sub

        Private Sub SendDlpReadUserInfo()
            dlpReadUserInfoSent = True
            SendPadpData(hostPadpTxId, New Byte() {&H10, &H0}, "DLP ReadUserInfo")
            hostPadpTxId = NextPadpTxId(hostPadpTxId)
        End Sub

        Private Sub SendDlpRequestWithArg(cmd As Byte, arg As Byte(), label As String)
            SendDlpRequestWithArg(cmd, DlpArgFirstId, arg, label)
        End Sub

        Private Sub SendDlpRequestWithArg(cmd As Byte, argId As Byte, arg As Byte(), label As String)
            Dim payload As Byte()
            If arg.Length = 0 Then
                payload = New Byte() {cmd, 0}
            ElseIf arg.Length < &HFF Then
                payload = New Byte(2 + 2 + arg.Length - 1) {}
                payload(0) = cmd
                payload(1) = 1
                payload(2) = argId
                payload(3) = CByte(arg.Length)
                Array.Copy(arg, 0, payload, 4, arg.Length)
            Else
                If arg.Length > &HFFFF Then
                    FailInstall($"{label} argument too large for DLP short arg: {arg.Length} bytes")
                    Return
                End If
                payload = New Byte(2 + 4 + arg.Length - 1) {}
                payload(0) = cmd
                payload(1) = 1
                payload(2) = CByte(argId Or &H80)
                payload(3) = 0
                WriteU16(payload, 4, CUShort(arg.Length))
                Array.Copy(arg, 0, payload, 6, arg.Length)
            End If
            installAwaitingTxId = hostPadpTxId
            SendPadpData(hostPadpTxId, payload, label)
            hostPadpTxId = NextPadpTxId(hostPadpTxId)
        End Sub

        Private Sub SendDlpWriteUserInfo()
            dlpWriteUserInfoSent = True
            Dim userName = Encoding.ASCII.GetBytes("ESP32-PALM" & ChrW(0))
            Dim argSize = 22 + userName.Length
            Dim payload(2 + 2 + argSize - 1) As Byte
            payload(0) = DlpCmdWriteUserInfo
            payload(1) = 1
            payload(2) = DlpArgFirstId
            payload(3) = CByte(argSize)
            WriteU32(payload, 4, &H45535032UI)
            WriteU32(payload, 8, 0UI)
            WriteU32(payload, 12, &H45535032UI)
            For i = 16 To 23
                payload(i) = 0
            Next
            payload(24) = &HFF
            payload(25) = CByte(userName.Length)
            Array.Copy(userName, 0, payload, 26, userName.Length)
            SendPadpData(hostPadpTxId, payload, "DLP WriteUserInfo")
            hostPadpTxId = NextPadpTxId(hostPadpTxId)
        End Sub

        Private Sub HandleDlpPayload(txId As Byte, payload As Byte())
            payload = NormalizeDlpPayload(txId, payload)
            If payload.Length < 4 Then
                Append($"HotSync short DLP payload tx ${txId:X2}")
                Return
            End If

            Dim cmd = CByte(payload(0) And &H7F)
            If (payload(0) And &H80) = 0 Then
                If payload(0) < &H10 Then
                    AppendHotSyncTrace($"RX ambiguous DLP tx=${txId:X2} len={payload.Length} bytes={FormatBytes(payload.Take(Math.Min(payload.Length, 96)).ToArray())}")
                End If
                HandleDlpRequestFromPalm(txId, payload)
                Return
            End If

            Dim argc = payload(1)
            Dim err = ReadU16(payload, 2)
            Dim quietWriteResource = installState = HotSyncInstallState.WritingResource AndAlso (cmd = DlpCmdWriteResource OrElse cmd = DlpCmdWriteRecord)
            AppendHotSyncTrace($"RX response tx=${txId:X2} cmd=${cmd:X2} argc={argc} err=${err:X4} state={installState} item={installResourceIndex}")
            If VerboseSerialLog AndAlso Not quietWriteResource Then
                Append($"DLP RX resp cmd ${cmd:X2} argc {argc} err ${err:X4} tx ${txId:X2}")
            End If
            If cmd = InstallExpectedResponseCommand() Then
                HandleInstallResponse(payload, err)
                Return
            End If
            If err <> 0 Then Return

            If cmd = DlpCmdReadUserInfo AndAlso argc > 0 Then
                Dim needsUserInfo = DecodeReadUserInfo(payload)
                If needsUserInfo AndAlso Not dlpWriteUserInfoSent Then SendDlpWriteUserInfo()
                If Not needsUserInfo Then BeginHotSyncWork()
            ElseIf cmd = DlpCmdWriteUserInfo Then
                If VerboseSerialLog Then Append("DLP WriteUserInfo accepted.")
                BeginHotSyncWork()
            End If
        End Sub

        Private Function NormalizeDlpPayload(txId As Byte, payload As Byte()) As Byte()
            If payload.Length >= 6 Then
                Dim prefixedLength = ReadU16(payload, 0)
                If prefixedLength = payload.Length - 2 AndAlso ((payload(2) And &H80) <> 0 OrElse payload(2) >= &H10) Then
                    Dim normalized(prefixedLength - 1) As Byte
                    Array.Copy(payload, 2, normalized, 0, normalized.Length)
                    AppendHotSyncTrace($"RX DLP length prefix stripped tx=${txId:X2} prefix={prefixedLength}")
                    Return normalized
                End If
            End If
            Return payload
        End Function

        Private Sub HandleDlpRequestFromPalm(txId As Byte, payload As Byte())
            Dim cmd = payload(0)
            Dim argc = payload(1)
            AppendHotSyncTrace($"RX request tx=${txId:X2} cmd=${cmd:X2} argc={argc} len={payload.Length} state={installState}")
            If VerboseSerialLog Then Append($"DLP RX req cmd ${cmd:X2} argc {argc} tx ${txId:X2} data {FormatBytes(payload)}")
            Select Case cmd
                Case DlpCmdReadSysInfo
                    SendDlpResponseArgs(txId, cmd, "DLP ReadSysInfo",
                                        BuildDlpArg(DlpArgFirstId, BuildReadSysInfoArg()),
                                        BuildDlpArg(DlpArgFirstId + 1, BuildReadSysInfoVersionArg()))
                Case DlpCmdReadStorageInfo
                    SendDlpResponseArgs(txId, cmd, "DLP ReadStorageInfo",
                                        BuildDlpArg(DlpArgFirstId, BuildReadStorageInfoArg()),
                                        BuildDlpArg(DlpArgFirstId + 1, BuildReadStorageInfoExArg()))
                Case DlpCmdEndOfSync
                    SendDlpResponseArgs(txId, cmd, "DLP EndOfSync")
                Case Else
                    SendDlpResponseArgs(txId, cmd, $"DLP cmd ${cmd:X2} empty")
            End Select
        End Sub

        Private Function InstallExpectedResponseCommand() As Byte
            Select Case installState
                Case HotSyncInstallState.DeleteSent
                    Return DlpCmdDeleteDB
                Case HotSyncInstallState.CreateSent
                    Return DlpCmdCreateDB
                Case HotSyncInstallState.AppBlockSent
                    Return DlpCmdWriteAppBlock
                Case HotSyncInstallState.SortBlockSent
                    Return DlpCmdWriteSortBlock
                Case HotSyncInstallState.WritingResource
                    If pendingInstall IsNot Nothing AndAlso pendingInstall.IsResourceDb Then Return DlpCmdWriteResource
                    Return DlpCmdWriteRecord
                Case HotSyncInstallState.CloseSent
                    Return DlpCmdCloseDB
                Case HotSyncInstallState.EndSent
                    Return DlpCmdEndOfSync
                Case Else
                    Return 0
            End Select
        End Function

        Private Sub BeginHotSyncWork()
            If installState <> HotSyncInstallState.Idle Then Return
            If pendingInstall Is Nothing Then PrepareNextPendingInstall()
            If pendingInstall Is Nothing Then
                Append("HotSync has no queued installs.")
                SendDlpEndOfSync()
                Return
            End If

            Append($"HotSync install starting: {pendingInstall.Name}")
            SendDlpDeleteDb()
        End Sub

        Private Sub PrepareNextPendingInstall()
            If pendingInstall IsNot Nothing OrElse pendingInstalls.Count = 0 Then Return

            pendingInstall = pendingInstalls.Dequeue()
            installDbId = 0
            installResourceIndex = 0
            installAwaitingTxId = 0
            installAppBlockDone = False
            installSortBlockDone = False
        End Sub

        Private Sub PulseCradleHotSync()
            If Not nativeReady Then Return

            NativeMusashi.palm_native_set_cradle_button(1)
            hotSyncButtonReleaseTimer.Stop()
            hotSyncButtonReleaseTimer.Start()
        End Sub

        Private Sub HotSyncButtonReleaseTimer_Tick(sender As Object, e As EventArgs)
            hotSyncButtonReleaseTimer.Stop()
            If nativeReady Then NativeMusashi.palm_native_set_cradle_button(0)
        End Sub

        Private Sub HandleInstallResponse(payload As Byte(), err As Integer)
            AppendHotSyncTrace($"Install response state={installState} err=${err:X4} db=${installDbId:X2} item={installResourceIndex}/{If(pendingInstall Is Nothing, 0, pendingInstall.ItemCount)}")
            Select Case installState
                Case HotSyncInstallState.DeleteSent
                    If err <> 0 AndAlso err <> 5 Then
                        FailInstall($"DeleteDB failed ${err:X4}")
                        Return
                    End If
                    SendDlpCreateDb()
                Case HotSyncInstallState.CreateSent
                    If err <> 0 Then
                        FailInstall($"CreateDB failed ${err:X4}")
                        Return
                    End If
                    installDbId = DecodeSingleByteArg(payload)
                    If VerboseSerialLog Then Append($"CreateDB OK db ${installDbId:X2}")
                    installResourceIndex = 0
                    SendNextInstallItem()
                Case HotSyncInstallState.AppBlockSent
                    If err <> 0 Then
                        FailInstall($"WriteAppBlock failed ${err:X4}")
                        Return
                    End If
                    installAppBlockDone = True
                    SendNextInstallItem()
                Case HotSyncInstallState.SortBlockSent
                    If err <> 0 Then
                        FailInstall($"WriteSortBlock failed ${err:X4}")
                        Return
                    End If
                    installSortBlockDone = True
                    SendNextInstallItem()
                Case HotSyncInstallState.WritingResource
                    If err <> 0 Then
                        FailInstall($"Write {pendingInstall.ItemKindName} {installResourceIndex} failed ${err:X4}")
                        Return
                    End If
                    installResourceIndex += 1
                    If installResourceIndex = pendingInstall.ItemCount OrElse installResourceIndex Mod 25 = 0 Then
                        Append($"HotSync {pendingInstall.ItemKindName}s written: {installResourceIndex}/{pendingInstall.ItemCount}")
                    End If
                    SendNextInstallItem()
                Case HotSyncInstallState.CloseSent
                    If err <> 0 Then
                        FailInstall($"CloseDB failed ${err:X4}")
                        Return
                    End If
                    Append($"HotSync install complete: {pendingInstall.Name}")
                    pendingInstall = Nothing
                    installState = HotSyncInstallState.Idle
                    If pendingInstalls.Count > 0 Then
                        PrepareNextPendingInstall()
                        Append($"HotSync install starting: {pendingInstall.Name}")
                        SendDlpDeleteDb()
                    Else
                        SendDlpEndOfSync()
                    End If
                Case HotSyncInstallState.EndSent
                    If err <> 0 Then
                        FailInstall($"EndOfSync failed ${err:X4}")
                        Return
                    End If
                    Append("HotSync install batch complete.")
                    installState = HotSyncInstallState.Done
            End Select
        End Sub

        Private Sub FailInstall(message As String)
            AppendHotSyncTrace($"FAIL {message}; state={installState}; db=${installDbId:X2}; item={installResourceIndex}; pending={If(pendingInstall Is Nothing, "(none)", pendingInstall.Name)}")
            Append(message)
            installState = HotSyncInstallState.Failed
            installAppBlockDone = False
            installSortBlockDone = False
        End Sub

        Private Sub SendDlpDeleteDb()
            Dim nameBytes = Encoding.ASCII.GetBytes(pendingInstall.Name & ChrW(0))
            Dim arg(2 + nameBytes.Length - 1) As Byte
            arg(0) = 0
            arg(1) = 0
            Array.Copy(nameBytes, 0, arg, 2, nameBytes.Length)
            SendDlpRequestWithArg(DlpCmdDeleteDB, arg, "DLP DeleteDB")
            installState = HotSyncInstallState.DeleteSent
        End Sub

        Private Sub SendDlpCreateDb()
            Dim nameBytes = Encoding.ASCII.GetBytes(pendingInstall.Name & ChrW(0))
            Dim arg(14 + nameBytes.Length - 1) As Byte
            WriteU32(arg, 0, pendingInstall.Creator)
            WriteU32(arg, 4, pendingInstall.DbType)
            arg(8) = 0
            arg(9) = 0
            WriteU16(arg, 10, CUShort(pendingInstall.Attributes))
            WriteU16(arg, 12, pendingInstall.Version)
            Array.Copy(nameBytes, 0, arg, 14, nameBytes.Length)
            SendDlpRequestWithArg(DlpCmdCreateDB, arg, "DLP CreateDB")
            installState = HotSyncInstallState.CreateSent
        End Sub

        Private Sub SendNextInstallItem()
            If installResourceIndex = 0 AndAlso pendingInstall.AppInfoBlock.Length > 0 AndAlso Not installAppBlockDone Then
                SendDlpWriteBlock(DlpCmdWriteAppBlock, pendingInstall.AppInfoBlock, "DLP WriteAppBlock")
                installState = HotSyncInstallState.AppBlockSent
                Return
            End If

            If installResourceIndex = 0 AndAlso pendingInstall.SortInfoBlock.Length > 0 AndAlso Not installSortBlockDone Then
                SendDlpWriteBlock(DlpCmdWriteSortBlock, pendingInstall.SortInfoBlock, "DLP WriteSortBlock")
                installState = HotSyncInstallState.SortBlockSent
                Return
            End If

            If installResourceIndex >= pendingInstall.ItemCount Then
                SendDlpCloseDb()
                Return
            End If

            If pendingInstall.IsResourceDb Then
                SendNextResource()
            Else
                SendNextRecord()
            End If
        End Sub

        Private Sub SendNextResource()
            Dim res = pendingInstall.Resources(installResourceIndex)
            If res.Data.Length > &HFFFF Then
                FailInstall($"Resource {installResourceIndex} too large for DLP 1.2: {res.Data.Length} bytes")
                Return
            End If

            Dim arg(10 + res.Data.Length - 1) As Byte
            arg(0) = installDbId
            arg(1) = 0
            WriteU32(arg, 2, res.ResType)
            WriteU16(arg, 6, res.ResId)
            WriteU16(arg, 8, CUShort(res.Data.Length))
            Array.Copy(res.Data, 0, arg, 10, res.Data.Length)
            SendDlpRequestWithArg(DlpCmdWriteResource, arg, $"DLP WriteResource {installResourceIndex + 1}/{pendingInstall.Resources.Count}")
            installState = HotSyncInstallState.WritingResource
        End Sub

        Private Sub SendNextRecord()
            Dim rec = pendingInstall.Records(installResourceIndex)
            If rec.Data.Length > &HFFFF Then
                FailInstall($"Record {installResourceIndex} too large for DLP 1.2: {rec.Data.Length} bytes")
                Return
            End If

            Dim arg(8 + rec.Data.Length - 1) As Byte
            arg(0) = installDbId
            arg(1) = &H80
            WriteU32(arg, 2, rec.RecordId)
            arg(6) = CByte(rec.Attributes And &HF0)
            arg(7) = CByte(rec.Attributes And &HF)
            Array.Copy(rec.Data, 0, arg, 8, rec.Data.Length)
            SendDlpRequestWithArg(DlpCmdWriteRecord, arg, $"DLP WriteRecord {installResourceIndex + 1}/{pendingInstall.Records.Count}")
            installState = HotSyncInstallState.WritingResource
        End Sub

        Private Sub SendDlpWriteBlock(cmd As Byte, block As Byte(), label As String)
            If block.Length > &HFFFF Then
                FailInstall($"{label} too large for DLP 1.2: {block.Length} bytes")
                Return
            End If

            Dim arg(4 + block.Length - 1) As Byte
            arg(0) = installDbId
            arg(1) = 0
            WriteU16(arg, 2, CUShort(block.Length))
            Array.Copy(block, 0, arg, 4, block.Length)
            SendDlpRequestWithArg(cmd, arg, label)
        End Sub

        Private Sub SendDlpCloseDb()
            SendDlpRequestWithArg(DlpCmdCloseDB, New Byte() {installDbId}, "DLP CloseDB")
            installState = HotSyncInstallState.CloseSent
        End Sub

        Private Sub SendDlpEndOfSync()
            SendDlpRequestWithArg(DlpCmdEndOfSync, New Byte() {0, 0}, "DLP EndOfSync")
            installState = HotSyncInstallState.EndSent
        End Sub

        Private Function DecodeReadUserInfo(payload As Byte()) As Boolean
            Dim argOffset = 4
            If payload.Length < argOffset + 2 Then Return True

            Dim argId = payload(argOffset)
            Dim argLength As Integer
            Dim dataOffset As Integer
            If (argId And &HC0) = &H80 Then
                If payload.Length < argOffset + 4 Then Return True
                argLength = ReadU16(payload, argOffset + 2)
                dataOffset = argOffset + 4
                argId = CByte(argId And &H7F)
            Else
                argLength = payload(argOffset + 1)
                dataOffset = argOffset + 2
            End If

            If argId <> DlpArgFirstId OrElse payload.Length < dataOffset + Math.Min(argLength, 30) Then Return True
            Dim userId = ReadU32(payload, dataOffset)
            Dim viewerId = ReadU32(payload, dataOffset + 4)
            Dim lastSyncPc = ReadU32(payload, dataOffset + 8)
            Dim userNameLen = payload(dataOffset + 28)
            Dim name = ""
            If userNameLen > 0 AndAlso payload.Length >= dataOffset + 30 + userNameLen Then
                name = Encoding.ASCII.GetString(payload, dataOffset + 30, userNameLen).TrimEnd(ChrW(0))
            End If

            If VerboseSerialLog Then Append($"DLP UserInfo user ${userId:X8} viewer ${viewerId:X8} pc ${lastSyncPc:X8} name '{name}'")
            Return userId = 0UI OrElse name.Length = 0
        End Function

        Private Sub SendPadpData(txId As Byte, payload As Byte(), label As String)
            If payload.Length > &HFFFF Then
                FailInstall($"{label} PADP payload too large: {payload.Length} bytes")
                Return
            End If
            If pendingPadpActive Then
                FailInstall($"{label} cannot start while PADP send is still active.")
                Return
            End If

            If label.StartsWith("DLP", StringComparison.Ordinal) Then
                AppendHotSyncTrace($"TX {label} tx=${txId:X2} len={payload.Length} state={installState}")
            End If

            pendingPadpPayload = payload
            pendingPadpLabel = label
            pendingPadpTxId = txId
            pendingPadpOffset = 0
            pendingPadpActive = True
            SendNextPadpFragment()
        End Sub

        Private Sub SendNextPadpFragment()
            If Not pendingPadpActive OrElse pendingPadpPayload Is Nothing Then Return

            Dim remaining = pendingPadpPayload.Length - pendingPadpOffset
            Dim count = Math.Min(PadpChunkSize, remaining)
            Dim last = pendingPadpOffset + count >= pendingPadpPayload.Length
            Dim flags As Byte = 0
            Dim sizeField As Integer
            If pendingPadpOffset = 0 Then
                flags = CByte(flags Or &H80)
                sizeField = pendingPadpPayload.Length
            Else
                sizeField = pendingPadpOffset
            End If
            If last Then flags = CByte(flags Or &H40)

            Dim padBody(4 + count - 1) As Byte
            padBody(0) = &H1
            padBody(1) = flags
            padBody(2) = CByte((sizeField >> 8) And &HFF)
            padBody(3) = CByte(sizeField And &HFF)
            Array.Copy(pendingPadpPayload, pendingPadpOffset, padBody, 4, count)

            Dim frame = BuildSlpFrame(3, 3, 2, pendingPadpTxId, padBody)
            Dim written = NativeMusashi.palm_native_uart_write_rx(frame, CUInt(frame.Length))
            If VerboseSerialLog AndAlso Not pendingPadpLabel.StartsWith("DLP WriteResource", StringComparison.Ordinal) AndAlso Not pendingPadpLabel.StartsWith("DLP WriteRecord", StringComparison.Ordinal) Then
                Dim suffix = If(pendingPadpPayload.Length > PadpChunkSize, $" frag {pendingPadpOffset}-{pendingPadpOffset + count - 1}/{pendingPadpPayload.Length}", "")
                Append($"UART RX {pendingPadpLabel}{suffix} {written}: tx ${pendingPadpTxId:X2} {FormatBytes(frame)}")
            End If
            pendingPadpOffset += count
            If last Then
                pendingPadpActive = False
                pendingPadpPayload = Nothing
                pendingPadpLabel = ""
            End If
        End Sub

        Private Sub SendDlpResponse(txId As Byte, cmd As Byte, args As Byte(), label As String)
            Dim payload(4 + args.Length - 1) As Byte
            payload(0) = CByte(cmd Or &H80)
            payload(1) = 0
            payload(2) = 0
            payload(3) = 0
            If args.Length > 0 Then
                payload(1) = 1
                Array.Copy(args, 0, payload, 4, args.Length)
            End If
            SendPadpData(txId, payload, label)
        End Sub

        Private Sub SendDlpResponseArgs(txId As Byte, cmd As Byte, label As String, ParamArray args As Byte()())
            Dim payloadLength = 4 + args.Sum(Function(arg) arg.Length)
            Dim payload(payloadLength - 1) As Byte
            payload(0) = CByte(cmd Or &H80)
            payload(1) = CByte(args.Length)
            payload(2) = 0
            payload(3) = 0

            Dim cursor = 4
            For Each arg In args
                Array.Copy(arg, 0, payload, cursor, arg.Length)
                cursor += arg.Length
            Next

            SendPadpData(txId, payload, label)
        End Sub

        Private Shared Function BuildDlpArg(argId As Integer, data As Byte()) As Byte()
            If data.Length > &HFE Then Throw New InvalidOperationException($"DLP response arg too large: {data.Length} bytes")

            Dim arg(2 + data.Length - 1) As Byte
            arg(0) = CByte(argId And &HFF)
            arg(1) = CByte(data.Length)
            Array.Copy(data, 0, arg, 2, data.Length)
            Return arg
        End Function

        Private Function BuildReadSysInfoArg() As Byte()
            Dim data(13) As Byte
            WriteU32(data, 0, PalmOsVersionForProfile())
            WriteU32(data, 4, 0UI)
            data(8) = 0
            data(9) = 4
            WriteU32(data, 10, ProductIdForProfile())
            Return data
        End Function

        Private Function BuildReadSysInfoVersionArg() As Byte()
            Dim data(11) As Byte
            WriteU16(data, 0, 1US)
            WriteU16(data, 2, 2US)
            WriteU16(data, 4, 1US)
            WriteU16(data, 6, 2US)
            WriteU32(data, 8, &HFFFFFFFFUI)
            Return data
        End Function

        Private Function BuildReadStorageInfoArg() As Byte()
            Dim cardName = Encoding.ASCII.GetBytes(PalmConfig.ProfileName & ChrW(0))
            Dim manufName = Encoding.ASCII.GetBytes("Palm" & ChrW(0))
            Dim cardInfoSize = 26 + cardName.Length + manufName.Length
            If (cardInfoSize And 1) <> 0 Then cardInfoSize += 1

            Dim data(4 + cardInfoSize - 1) As Byte
            data(0) = 0
            data(1) = 0
            data(2) = 0
            data(3) = 1

            Dim cursor = 4
            data(cursor) = CByte(cardInfoSize)
            data(cursor + 1) = 0
            WriteU16(data, cursor + 2, 1US)
            WritePalmDateTime(data, cursor + 4, New DateTime(2026, 1, 1, 0, 0, 0))
            WriteU32(data, cursor + 12, RomSizeForProfile())
            WriteU32(data, cursor + 16, PalmConfig.RamActivePreset)
            WriteU32(data, cursor + 20, Math.Max(0UI, PalmConfig.RamActivePreset \ 2UI))
            data(cursor + 24) = CByte(cardName.Length)
            data(cursor + 25) = CByte(manufName.Length)
            Array.Copy(cardName, 0, data, cursor + 26, cardName.Length)
            Array.Copy(manufName, 0, data, cursor + 26 + cardName.Length, manufName.Length)
            Return data
        End Function

        Private Shared Function BuildReadStorageInfoExArg() As Byte()
            Dim data(19) As Byte
            Return data
        End Function

        Private Shared Sub WritePalmDateTime(buffer As Byte(), offset As Integer, value As DateTime)
            WriteU16(buffer, offset, CUShort(value.Year))
            buffer(offset + 2) = CByte(value.Month)
            buffer(offset + 3) = CByte(value.Day)
            buffer(offset + 4) = CByte(value.Hour)
            buffer(offset + 5) = CByte(value.Minute)
            buffer(offset + 6) = CByte(value.Second)
            buffer(offset + 7) = 0
        End Sub

        Private Shared Function PalmOsVersionForProfile() As UInteger
            Select Case PalmConfig.ActiveHardwareProfile
                Case PalmConfig.HardwareProfile.IIIcExperimental
                    Return &H04100000UI
                Case PalmConfig.HardwareProfile.M100Experimental
                    Return &H03510000UI
                Case Else
                    Return &H03300000UI
            End Select
        End Function

        Private Shared Function ProductIdForProfile() As UInteger
            Select Case PalmConfig.ActiveHardwareProfile
                Case PalmConfig.HardwareProfile.IIIcExperimental
                    Return &H49494963UI ' "IIIc"
                Case PalmConfig.HardwareProfile.M100Experimental
                    Return &H6D313030UI ' "m100"
                Case Else
                    Return &H49494978UI ' "IIIx"
            End Select
        End Function

        Private Shared Function RomSizeForProfile() As UInteger
            Select Case PalmConfig.ActiveHardwareProfile
                Case PalmConfig.HardwareProfile.IIIcExperimental
                    Return 2097152UI
                Case Else
                    Return 1048576UI
            End Select
        End Function

        Private Sub AppendHotSyncTrace(message As String)
            If Not EnableHotSyncTrace Then Return
            Try
                File.AppendAllText(hotSyncTracePath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}")
            Catch
            End Try
        End Sub

        Private Shared Function NextPadpTxId(value As Byte) As Byte
            If value >= &HFE Then Return 1
            Return CByte(value + 1)
        End Function

        Private Shared Function BuildSlpFrame(dest As Byte, src As Byte, packetType As Byte, txId As Byte, body As Byte()) As Byte()
            Dim frame(10 + body.Length + 2 - 1) As Byte
            frame(0) = &HBE
            frame(1) = &HEF
            frame(2) = &HED
            frame(3) = dest
            frame(4) = src
            frame(5) = packetType
            frame(6) = CByte((body.Length >> 8) And &HFF)
            frame(7) = CByte(body.Length And &HFF)
            frame(8) = txId

            Dim headerSum As Integer = 0
            For i = 0 To 8
                headerSum = (headerSum + frame(i)) And &HFF
            Next
            frame(9) = CByte(headerSum)

            Array.Copy(body, 0, frame, 10, body.Length)
            Dim crc = Crc16(frame, 10 + body.Length)
            frame(10 + body.Length) = CByte((crc >> 8) And &HFF)
            frame(11 + body.Length) = CByte(crc And &HFF)
            Return frame
        End Function

        Private Shared Function ReadU16(buffer As Byte(), offset As Integer) As Integer
            Return (CInt(buffer(offset)) << 8) Or CInt(buffer(offset + 1))
        End Function

        Private Shared Function ReadU32(buffer As Byte(), offset As Integer) As UInteger
            Return (CUInt(buffer(offset)) << 24) Or
                (CUInt(buffer(offset + 1)) << 16) Or
                (CUInt(buffer(offset + 2)) << 8) Or
                CUInt(buffer(offset + 3))
        End Function

        Private Shared Function DecodeSingleByteArg(payload As Byte()) As Byte
            If payload.Length < 7 Then Return 0
            Dim offset = 4
            If (payload(offset) And &HC0) = &H80 Then
                If payload.Length < 9 Then Return 0
                Return payload(offset + 4)
            End If
            Return payload(offset + 2)
        End Function

        Private Shared Sub WriteU16(buffer As Byte(), offset As Integer, value As UShort)
            buffer(offset) = CByte((value >> 8) And &HFFUS)
            buffer(offset + 1) = CByte(value And &HFFUS)
        End Sub

        Private Shared Sub WriteU32(buffer As Byte(), offset As Integer, value As UInteger)
            buffer(offset) = CByte((value >> 24) And &HFFUI)
            buffer(offset + 1) = CByte((value >> 16) And &HFFUI)
            buffer(offset + 2) = CByte((value >> 8) And &HFFUI)
            buffer(offset + 3) = CByte(value And &HFFUI)
        End Sub

        Private Shared Function Crc16(buffer As Byte(), count As Integer) As Integer
            Dim crc As Integer = 0
            For i = 0 To count - 1
                crc = ((crc << 8) Xor Crc16Table(((crc >> 8) Xor buffer(i)) And &HFF)) And &HFFFF
            Next
            Return crc
        End Function

        Private Shared Function Crc16X25(buffer As Byte(), count As Integer) As Integer
            Dim crc As Integer = &HFFFF
            For i = 0 To count - 1
                crc = crc Xor buffer(i)
                For bit = 0 To 7
                    If (crc And 1) <> 0 Then
                        crc = ((crc >> 1) Xor &H8408) And &HFFFF
                    Else
                        crc = (crc >> 1) And &HFFFF
                    End If
                Next
            Next
            Return (Not crc) And &HFFFF
        End Function

        Private Shared ReadOnly Crc16Table As UShort() = {
            &H0US, &H1021US, &H2042US, &H3063US, &H4084US, &H50A5US, &H60C6US, &H70E7US, &H8108US, &H9129US, &HA14AUS, &HB16BUS, &HC18CUS, &HD1ADUS, &HE1CEUS, &HF1EFUS,
            &H1231US, &H210US, &H3273US, &H2252US, &H52B5US, &H4294US, &H72F7US, &H62D6US, &H9339US, &H8318US, &HB37BUS, &HA35AUS, &HD3BDUS, &HC39CUS, &HF3FFUS, &HE3DEUS,
            &H2462US, &H3443US, &H420US, &H1401US, &H64E6US, &H74C7US, &H44A4US, &H5485US, &HA56AUS, &HB54BUS, &H8528US, &H9509US, &HE5EEUS, &HF5CFUS, &HC5ACUS, &HD58DUS,
            &H3653US, &H2672US, &H1611US, &H630US, &H76D7US, &H66F6US, &H5695US, &H46B4US, &HB75BUS, &HA77AUS, &H9719US, &H8738US, &HF7DFUS, &HE7FEUS, &HD79DUS, &HC7BCUS,
            &H48C4US, &H58E5US, &H6886US, &H78A7US, &H840US, &H1861US, &H2802US, &H3823US, &HC9CCUS, &HD9EDUS, &HE98EUS, &HF9AFUS, &H8948US, &H9969US, &HA90AUS, &HB92BUS,
            &H5AF5US, &H4AD4US, &H7AB7US, &H6A96US, &H1A71US, &HA50US, &H3A33US, &H2A12US, &HDBFDUS, &HCBDCUS, &HFBBFUS, &HEB9EUS, &H9B79US, &H8B58US, &HBB3BUS, &HAB1AUS,
            &H6CA6US, &H7C87US, &H4CE4US, &H5CC5US, &H2C22US, &H3C03US, &HC60US, &H1C41US, &HEDAEUS, &HFD8FUS, &HCDECUS, &HDDCDUS, &HAD2AUS, &HBD0BUS, &H8D68US, &H9D49US,
            &H7E97US, &H6EB6US, &H5ED5US, &H4EF4US, &H3E13US, &H2E32US, &H1E51US, &HE70US, &HFF9FUS, &HEFBEUS, &HDFDDUS, &HCFFCUS, &HBF1BUS, &HAF3AUS, &H9F59US, &H8F78US,
            &H9188US, &H81A9US, &HB1CAUS, &HA1EBUS, &HD10CUS, &HC12DUS, &HF14EUS, &HE16FUS, &H1080US, &HA1US, &H30C2US, &H20E3US, &H5004US, &H4025US, &H7046US, &H6067US,
            &H83B9US, &H9398US, &HA3FBUS, &HB3DAUS, &HC33DUS, &HD31CUS, &HE37FUS, &HF35EUS, &H2B1US, &H1290US, &H22F3US, &H32D2US, &H4235US, &H5214US, &H6277US, &H7256US,
            &HB5EAUS, &HA5CBUS, &H95A8US, &H8589US, &HF56EUS, &HE54FUS, &HD52CUS, &HC50DUS, &H34E2US, &H24C3US, &H14A0US, &H481US, &H7466US, &H6447US, &H5424US, &H4405US,
            &HA7DBUS, &HB7FAUS, &H8799US, &H97B8US, &HE75FUS, &HF77EUS, &HC71DUS, &HD73CUS, &H26D3US, &H36F2US, &H691US, &H16B0US, &H6657US, &H7676US, &H4615US, &H5634US,
            &HD94CUS, &HC96DUS, &HF90EUS, &HE92FUS, &H99C8US, &H89E9US, &HB98AUS, &HA9ABUS, &H5844US, &H4865US, &H7806US, &H6827US, &H18C0US, &H8E1US, &H3882US, &H28A3US,
            &HCB7DUS, &HDB5CUS, &HEB3FUS, &HFB1EUS, &H8BF9US, &H9BD8US, &HABBBUS, &HBB9AUS, &H4A75US, &H5A54US, &H6A37US, &H7A16US, &HAF1US, &H1AD0US, &H2AB3US, &H3A92US,
            &HFD2EUS, &HED0FUS, &HDD6CUS, &HCD4DUS, &HBDAAUS, &HAD8BUS, &H9DE8US, &H8DC9US, &H7C26US, &H6C07US, &H5C64US, &H4C45US, &H3CA2US, &H2C83US, &H1CE0US, &HCC1US,
            &HEF1FUS, &HFF3EUS, &HCF5DUS, &HDF7CUS, &HAF9BUS, &HBFBAUS, &H8FD9US, &H9FF8US, &H6E17US, &H7E36US, &H4E55US, &H5E74US, &H2E93US, &H3EB2US, &HED1US, &H1EF0US
        }

        Private Shared Function FormatBytes(bytes As IEnumerable(Of Byte)) As String
            Dim sb As New StringBuilder()
            For Each value In bytes
                If sb.Length > 0 Then sb.Append(" "c)
                sb.Append(value.ToString("X2", CultureInfo.InvariantCulture))
            Next
            Return sb.ToString()
        End Function

        Private Shared Function SanitizeBmpFileName(fileName As String) As String
            Dim invalid = Path.GetInvalidFileNameChars()
            Dim sb As New StringBuilder(fileName.Length)
            For Each ch In fileName
                sb.Append(If(invalid.Contains(ch), "_"c, ch))
            Next
            Dim sanitized = sb.ToString().Trim()
            If sanitized.Length = 0 Then sanitized = "note.bmp"
            Return sanitized
        End Function

        Private NotInheritable Class PalmDbImage
            Public Property Name As String = ""
            Public Property Attributes As UShort
            Public Property Version As UShort
            Public Property DbType As UInteger
            Public Property Creator As UInteger
            Public Property AppInfoBlock As Byte() = Array.Empty(Of Byte)()
            Public Property SortInfoBlock As Byte() = Array.Empty(Of Byte)()
            Public Property Resources As New List(Of PalmResource)
            Public Property Records As New List(Of PalmRecord)

            Public ReadOnly Property IsResourceDb As Boolean
                Get
                    Return (Attributes And &H1US) <> 0
                End Get
            End Property

            Public ReadOnly Property ItemCount As Integer
                Get
                    Return If(IsResourceDb, Resources.Count, Records.Count)
                End Get
            End Property

            Public ReadOnly Property ItemKindName As String
                Get
                    Return If(IsResourceDb, "resource", "record")
                End Get
            End Property

            Public Shared Function Parse(bytes As Byte(), fileName As String) As PalmDbImage
                If bytes Is Nothing OrElse bytes.Length < 78 Then Throw New InvalidDataException("Palm database image is too small.")
                Dim image As New PalmDbImage()
                Dim zero = Array.IndexOf(bytes, CByte(0), 0, Math.Min(32, bytes.Length))
                If zero < 0 Then zero = 32
                image.Name = Encoding.ASCII.GetString(bytes, 0, zero)
                If image.Name.Length = 0 Then image.Name = Path.GetFileNameWithoutExtension(fileName)
                image.Attributes = ReadBe16(bytes, 32)
                image.Version = ReadBe16(bytes, 34)
                Dim appInfoOffset = CInt(ReadBe32(bytes, 52))
                Dim sortInfoOffset = CInt(ReadBe32(bytes, 56))
                image.DbType = ReadBe32(bytes, 60)
                image.Creator = ReadBe32(bytes, 64)
                Dim count = ReadBe16(bytes, 76)
                Dim listOffset = 78
                Dim entrySize = If(image.IsResourceDb, 10, 8)
                If bytes.Length < listOffset + count * entrySize Then Throw New InvalidDataException("Palm database record list is truncated.")

                Dim dataOffsets As New List(Of Integer)
                If appInfoOffset > 0 Then dataOffsets.Add(appInfoOffset)
                If sortInfoOffset > 0 Then dataOffsets.Add(sortInfoOffset)

                For i = 0 To count - 1
                    Dim entry = listOffset + i * entrySize
                    dataOffsets.Add(CInt(ReadBe32(bytes, entry + If(image.IsResourceDb, 6, 0))))
                Next
                dataOffsets.Sort()

                If appInfoOffset > 0 Then image.AppInfoBlock = CopyBlock(bytes, appInfoOffset, NextDataOffset(dataOffsets, appInfoOffset, bytes.Length), "app info")
                If sortInfoOffset > 0 Then image.SortInfoBlock = CopyBlock(bytes, sortInfoOffset, NextDataOffset(dataOffsets, sortInfoOffset, bytes.Length), "sort info")

                If image.IsResourceDb Then
                    For i = 0 To count - 1
                        Dim entry = listOffset + i * entrySize
                        Dim resType = ReadBe32(bytes, entry)
                        Dim resId = ReadBe16(bytes, entry + 4)
                        Dim dataOffset = CInt(ReadBe32(bytes, entry + 6))
                        Dim nextOffset = NextDataOffset(dataOffsets, dataOffset, bytes.Length)
                        Dim data = CopyBlock(bytes, dataOffset, nextOffset, $"resource {i}")
                        image.Resources.Add(New PalmResource With {.ResType = resType, .ResId = resId, .Data = data})
                    Next
                Else
                    For i = 0 To count - 1
                        Dim entry = listOffset + i * entrySize
                        Dim dataOffset = CInt(ReadBe32(bytes, entry))
                        Dim attrs = bytes(entry + 4)
                        Dim recordId = (CUInt(bytes(entry + 5)) << 16) Or (CUInt(bytes(entry + 6)) << 8) Or CUInt(bytes(entry + 7))
                        Dim nextOffset = NextDataOffset(dataOffsets, dataOffset, bytes.Length)
                        Dim data = CopyBlock(bytes, dataOffset, nextOffset, $"record {i}")
                        image.Records.Add(New PalmRecord With {.RecordId = recordId, .Attributes = attrs, .Data = data})
                    Next
                End If

                Return image
            End Function

            Private Shared Function NextDataOffset(offsets As List(Of Integer), currentOffset As Integer, fileLength As Integer) As Integer
                For Each offset In offsets
                    If offset > currentOffset Then Return offset
                Next
                Return fileLength
            End Function

            Private Shared Function CopyBlock(bytes As Byte(), startOffset As Integer, endOffset As Integer, label As String) As Byte()
                If startOffset < 0 OrElse endOffset < startOffset OrElse endOffset > bytes.Length Then Throw New InvalidDataException($"Invalid {label} offset.")
                If endOffset = startOffset Then Return Array.Empty(Of Byte)()
                Dim data(endOffset - startOffset - 1) As Byte
                Array.Copy(bytes, startOffset, data, 0, data.Length)
                Return data
            End Function

            Private Shared Function ReadBe16(bytes As Byte(), offset As Integer) As UShort
                Return CUShort((CUInt(bytes(offset)) << 8) Or CUInt(bytes(offset + 1)))
            End Function

            Private Shared Function ReadBe32(bytes As Byte(), offset As Integer) As UInteger
                Return (CUInt(bytes(offset)) << 24) Or
                    (CUInt(bytes(offset + 1)) << 16) Or
                    (CUInt(bytes(offset + 2)) << 8) Or
                    CUInt(bytes(offset + 3))
            End Function
        End Class

        Private NotInheritable Class PalmResource
            Public Property ResType As UInteger
            Public Property ResId As UShort
            Public Property Data As Byte()
        End Class

        Private NotInheritable Class PalmRecord
            Public Property RecordId As UInteger
            Public Property Attributes As Byte
            Public Property Data As Byte()
        End Class

        Private Function ShouldRefreshAutoLcd() As Boolean
            If NativeMusashi.palm_native_lcd_dirty() = 0 Then Return False

            Dim nowTick = Environment.TickCount64
            Dim redrawInterval = CLng(PalmConfig.DefaultLcdRedrawIntervalMs)
            If lastAutoLcdTick <> 0 AndAlso nowTick - lastAutoLcdTick < redrawInterval Then Return False

            Dim nativeDebug As New PalmNativeDebug()
            NativeMusashi.palm_native_get_debug(nativeDebug)

            lastAutoLcdWriteCount = nativeDebug.LcdWriteCount
            lastAutoLcdBase = NativeMusashi.palm_native_lcd_start()
            lastAutoLcdPitch = NativeMusashi.palm_native_lcd_pitch()
            lastAutoLcdPanel = NativeMusashi.palm_native_lcd_panel()
            lastAutoLcdPan = NativeMusashi.palm_native_lcd_pan()
            lastAutoLcdTick = nowTick
            Return True
        End Function

        Private Sub RunNativeSlices(sliceCount As Integer, cyclesPerSlice As Integer, Optional dumpTrace As Boolean = True)
            If Not nativeReady Then
                InitCpuButton_Click(Me, EventArgs.Empty)
                If Not nativeReady Then Return
            End If

            NativeMusashi.palm_native_set_trace_enabled(If(dumpTrace, 1, 0))
            Dim startPc = NativeMusashi.palm_native_get_pc()
            Dim startA2 = NativeMusashi.palm_native_get_a(2)
            For i = 1 To sliceCount
                NativeMusashi.palm_native_execute(cyclesPerSlice)
            Next
            NativeMusashi.palm_native_set_trace_enabled(0)

            nativeSlices += CUInt(sliceCount)
            RefreshDebugStatus()
            Dim endPc = NativeMusashi.palm_native_get_pc()
            Dim endA2 = NativeMusashi.palm_native_get_a(2)
            Dim endA4 = NativeMusashi.palm_native_get_a(4)
            Append($"Ran {sliceCount} slices x {cyclesPerSlice} cycles, PC ${startPc:X8}->${endPc:X8}, A2 ${startA2:X8}->${endA2:X8}, A4 ${endA4:X8}.")
            If dumpTrace Then
                AppendNativeFirstPcTrace()
                AppendNativeJumpTrace()
                AppendNativePcTrace()
                AppendNativeTrace()
            Else
                Append("Trace skipped for fast run. Use a diagnostic run when you need detail.")
                Append("")
            End If
        End Sub

        Private Sub LcdPanel_PenChanged(down As Boolean, x As Integer, y As Integer)
            If Not nativeReady Then Return

            If powerWakeTouchActive OrElse NativeMusashi.palm_native_is_asleep() <> 0 Then
                penUpTimer.Stop()
                NativeMusashi.palm_native_set_pen(0, 0, 0)
                powerWakeTouchActive = down
                If down Then
                    WakeSleepingPalmWithPowerButton()
                End If
                Return
            End If

            Dim tx = x
            Dim ty = y
            If TouchSwapAxes Then
                Dim t = tx
                tx = Math.Min(PalmConfig.DigitizerWidth - 1, ty)
                ty = t
            End If
            Dim centerX = (PalmConfig.DigitizerWidth - 1) / 2.0
            Dim centerY = (PalmConfig.DigitizerHeight - 1) / 2.0
            tx = CInt(Math.Round(((tx - centerX) * TouchScaleXPercent / 100.0) + centerX))
            ty = CInt(Math.Round(((ty - centerY) * TouchScaleYPercent / 100.0) + centerY))
            If TouchInvertX Then tx = PalmConfig.DigitizerWidth - 1 - tx
            If TouchInvertY Then ty = PalmConfig.DigitizerHeight - 1 - ty
            tx = Math.Max(0, Math.Min(PalmConfig.DigitizerWidth - 1, tx + TouchOffsetX))
            ty = Math.Max(0, Math.Min(PalmConfig.DigitizerHeight - 1, ty + TouchOffsetY))

            lastTouchX = tx
            lastTouchY = ty
            Dim isMove = down AndAlso lastPenDown
            If down Then
                penUpTimer.Stop()
                SendPenToNative(True, tx, ty)
            Else
                penUpTimer.Stop()
                If TouchHoldMs <= 0 Then
                    SendPenToNative(False, tx, ty)
                Else
                    penUpTimer.Interval = Math.Max(1, TouchHoldMs)
                    penUpTimer.Start()
                End If
            End If
            lastPenDown = down

            Dim nowTick = Environment.TickCount64
            If Not autoRunTimer.Enabled AndAlso (Not isMove OrElse nowTick - lastTouchLogTick >= 150) Then
                If UseRawAdcTouch Then
                    Dim raw = ToAdcRaw(tx, ty)
                    Append($"Pen {(If(down, "down", "up"))} {x},{y} -> {tx},{ty} ADC {raw.RawX},{raw.RawY}")
                Else
                    Append($"Pen {(If(down, "down", "up"))} {x},{y} -> {tx},{ty}")
                End If
                lastTouchLogTick = nowTick
            End If
            If autoRunTimer.Enabled Then
                RunTouchSlices(If(isMove, 2, 6), 5000, True)
            ElseIf isMove Then
                RunTouchSlices(8, 2000, True)
            Else
                RunTouchSlices(40, 2000, True)
            End If
        End Sub

        Private Sub PenUpTimer_Tick(sender As Object, e As EventArgs)
            penUpTimer.Stop()
            If Not nativeReady Then Return

            SendPenToNative(False, lastTouchX, lastTouchY)
            If autoRunTimer.Enabled Then
                RunTouchSlices(6, 5000)
            End If
        End Sub

        Private Sub SendPenToNative(down As Boolean, x As Integer, y As Integer)
            If UseRawAdcTouch Then
                Dim raw = ToAdcRaw(x, y)
                NativeMusashi.palm_native_set_pen_raw(If(down, 1, 0), raw.RawX, raw.RawY)
            Else
                NativeMusashi.palm_native_set_pen(If(down, 1, 0), CUShort(x), CUShort(y))
            End If
        End Sub

        Private Sub RunTouchSlices(sliceCount As Integer, cyclesPerSlice As Integer, Optional syncAds As Boolean = False)
            Dim startAds As UInteger = 0UI
            If syncAds AndAlso SyncAdsAfterTouch Then
                Dim startDebug As New PalmNativeDebug()
                NativeMusashi.palm_native_get_debug(startDebug)
                startAds = startDebug.AdsCommandCount
            End If

            For i = 1 To sliceCount
                NativeMusashi.palm_native_execute(cyclesPerSlice)
            Next
            nativeSlices += CUInt(sliceCount)

            If syncAds AndAlso SyncAdsAfterTouch Then
                Dim targetAds = If(lastPenDown, 2UI, 1UI)
                Dim extraSlices = 0
                While extraSlices < 24
                    Dim currentDebug As New PalmNativeDebug()
                    NativeMusashi.palm_native_get_debug(currentDebug)
                    If currentDebug.AdsCommandCount - startAds >= targetAds Then Exit While

                    NativeMusashi.palm_native_execute(cyclesPerSlice)
                    nativeSlices += 1UI
                    extraSlices += 1
                End While
            End If

            If ShouldRefreshAutoLcd() Then UpdateNativeLcdPreview()
        End Sub

        Private Sub SendButtonBitsToNative(bits As UShort, down As Boolean, label As String)
            If Not nativeReady Then
                InitCpuButton_Click(Me, EventArgs.Empty)
                If Not nativeReady Then Return
            End If

            penUpTimer.Stop()
            Dim wasAsleep = NativeMusashi.palm_native_is_asleep() <> 0
            NativeMusashi.palm_native_set_pen(0, 0, 0)
            NativeMusashi.palm_native_set_button_bits(bits, If(down, 1, 0))

            If Not autoRunTimer.Enabled Then Append($"{label} {(If(down, "down", "up"))}")
            If wasAsleep AndAlso Not down Then
                RefreshDebugStatus()
                Return
            End If
            If autoRunTimer.Enabled Then
                For i = 1 To 12
                    NativeMusashi.palm_native_execute(10000)
                Next
                nativeSlices += 12UI
                UpdateNativeLcdPreview()
            Else
                RunNativeSlices(100, 2000, False)
            End If
        End Sub

        Private Sub WakeSleepingPalmWithPowerButton()
            If Not nativeReady Then Return

            penUpTimer.Stop()
            NativeMusashi.palm_native_set_pen(0, 0, 0)
            AppendWakeDebug("before")
            NativeMusashi.palm_native_set_button_bits(KeyBitPower, 1)
            AppendWakeDebug("power-down")

            Dim wakeCycles As UInteger = 0UI
            For i = 1 To 160
                Dim ran = NativeMusashi.palm_native_service_wake(10000)
                wakeCycles += CUInt(Math.Max(0, ran))
                If i = 1 OrElse i = 8 OrElse i = 40 OrElse i = 160 OrElse NativeMusashi.palm_native_is_asleep() = 0 Then
                    AppendWakeDebug($"service-{i}", ran)
                End If
                If NativeMusashi.palm_native_is_asleep() = 0 Then Exit For
            Next

            NativeMusashi.palm_native_set_button_bits(KeyBitPower, 0)
            AppendWakeDebug("power-up")

            For i = 1 To 24
                NativeMusashi.palm_native_execute(10000)
            Next

            nativeSlices += 184UI
            AppendWakeDebug($"after-settle/{wakeCycles}")
            UpdateNativeLcdPreview()
        End Sub

        Private Function NativeReg16(offset As UInteger) As UShort
            Dim aligned = offset And Not 3UI
            Dim value = NativeMusashi.palm_native_probe32(&HFFFFF000UI + aligned)
            If (offset And 2UI) = 0UI Then
                Return CUShort((value >> 16) And &HFFFFUI)
            End If
            Return CUShort(value And &HFFFFUI)
        End Function

        Private Function NativeReg8(offset As UInteger) As Byte
            Return NativeMusashi.palm_native_peek8(&HFFFFF000UI + offset)
        End Function

        Private Function SedReg8(offset As UInteger) As Byte
            Return NativeMusashi.palm_native_peek8(&H1F01FFE0UI + offset)
        End Function

        Private Sub AppendWakeDebug(label As String, Optional ran As Integer = -1)
            If Not nativeReady OrElse PalmConfig.ActiveHardwareProfile <> PalmConfig.HardwareProfile.IIIcExperimental Then Return

            Dim pc = NativeMusashi.palm_native_get_pc()
            Dim sr = NativeMusashi.palm_native_get_sr()
            Dim pll = NativeReg16(&H200UI)
            Dim imrHi = NativeReg16(&H304UI)
            Dim imrLo = NativeReg16(&H306UI)
            Dim isrHi = NativeReg16(&H30CUI)
            Dim isrLo = NativeReg16(&H30EUI)
            Dim iprHi = NativeReg16(&H310UI)
            Dim iprLo = NativeReg16(&H312UI)
            Dim pcDir = NativeReg8(&H410UI)
            Dim pcData = NativeReg8(&H411UI)
            Dim pcSel = NativeReg8(&H413UI)
            Dim pdDir = NativeReg8(&H418UI)
            Dim pdData = NativeReg8(&H419UI)
            Dim pdPol = NativeReg8(&H41CUI)
            Dim pdReq = NativeReg8(&H41DUI)
            Dim pdKbd = NativeReg8(&H41EUI)
            Dim pdEdge = NativeReg8(&H41FUI)
            Dim pfDir = NativeReg8(&H428UI)
            Dim pfData = NativeReg8(&H429UI)
            Dim pfSel = NativeReg8(&H42BUI)
            Dim sed03 = SedReg8(&H03UI)
            Dim sed04 = SedReg8(&H04UI)
            Dim sed05 = SedReg8(&H05UI)

            Append($"Wake {label}: ran={ran} asleep={NativeMusashi.palm_native_is_asleep()} PC=${pc:X8} SR=${sr:X4} PLL=${pll:X4} IMR=${imrHi:X4}/{imrLo:X4} ISR=${isrHi:X4}/{isrLo:X4} IPR=${iprHi:X4}/{iprLo:X4} PC=${pcDir:X2}/{pcData:X2}/{pcSel:X2} PD=${pdDir:X2}/{pdData:X2}/{pdPol:X2}/{pdReq:X2}/{pdKbd:X2}/{pdEdge:X2} PF=${pfDir:X2}/{pfData:X2}/{pfSel:X2} SED03=${sed03:X2} SED04=${sed04:X2} SED05=${sed05:X2}")
        End Sub

        Private Function ToAdcRaw(x As Integer, y As Integer) As (RawX As UShort, RawY As UShort)
            Dim clampedX = Math.Max(0, Math.Min(PalmConfig.DigitizerWidth - 1, x))
            Dim clampedY = Math.Max(0, Math.Min(PalmConfig.DigitizerHeight - 1, y))
            Dim rawX = MapToRaw(clampedX, PalmConfig.DigitizerWidth - 1, TouchRawXMin, TouchRawXMax)
            Dim rawY = MapToRaw(clampedY, PalmConfig.DigitizerHeight - 1, TouchRawYMin, TouchRawYMax)
            Return (CUShort(Math.Max(0, Math.Min(4095, rawX))), CUShort(Math.Max(0, Math.Min(4095, rawY))))
        End Function

        Private Shared Function MapToRaw(value As Integer, maxValue As Integer, rawMin As Integer, rawMax As Integer) As Integer
            Dim t = value / CDbl(Math.Max(1, maxValue))
            Return CInt(Math.Round(rawMin + ((rawMax - rawMin) * t)))
        End Function

        Private Sub AppendNativeFirstPcTrace()
            Dim trace As New StringBuilder(8192)
            NativeMusashi.palm_native_copy_first_pc_trace(trace, CUInt(trace.Capacity))
            If trace.Length = 0 Then Return

            Append("First native instruction PCs:")
            Append(trace.ToString().TrimEnd())
            Append("")
        End Sub

        Private Sub AppendNativeJumpTrace()
            Dim trace As New StringBuilder(8192)
            NativeMusashi.palm_native_copy_jump_trace(trace, CUInt(trace.Capacity))
            If trace.Length = 0 Then Return

            Append("Recent native PC jumps:")
            Append(trace.ToString().TrimEnd())
            Append("")
        End Sub

        Private Sub AppendNativePcTrace()
            Dim trace As New StringBuilder(8192)
            NativeMusashi.palm_native_copy_pc_trace(trace, CUInt(trace.Capacity))
            If trace.Length = 0 Then Return

            Append("Recent native instruction PCs:")
            Append(trace.ToString().TrimEnd())
            Append("")
        End Sub

        Private Sub AppendNativeTrace()
            Dim trace As New StringBuilder(8192)
            NativeMusashi.palm_native_copy_trace(trace, CUInt(trace.Capacity))
            If trace.Length = 0 Then Return

            Append("Recent native register writes:")
            Append(trace.ToString().TrimEnd())
            Append("")
        End Sub

        Private Shared Function TryParseHex(text As String, ByRef value As UInteger) As Boolean
            Dim cleaned = text.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase).Replace("$", "")
            Return UInteger.TryParse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture, value)
        End Function

        Private Sub Append(message As String)
            System.Diagnostics.Debug.WriteLine(message)
        End Sub
    End Class
End Namespace
