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
        Private lastUartLogTick As Long
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
        Private memoDbId As Byte
        Private memoRecordCount As Integer
        Private memoRecordIndex As Integer
        Private memoWriteIndex As Integer
        Private ReadOnly memoRecords As New List(Of MemoRecordMirror)
        Private ReadOnly memoPendingWrites As New List(Of MemoRecordMirror)
        Private noteDbId As Byte
        Private noteDbNameIndex As Integer
        Private noteDbListStartIndex As Integer
        Private noteRecordCount As Integer
        Private noteRecordIndex As Integer
        Private ReadOnly noteDbNameCandidates As New List(Of String)
        Private ReadOnly dbListEntries As New List(Of DbListEntry)
        Private ReadOnly noteRecords As New List(Of NotePadRecordMirror)
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
        Private Const PadpChunkSize As Integer = 200
        Private Const PalmMemoMaxBytes As Integer = 4096
        Private Const DlpCmdReadUserInfo As Byte = &H10
        Private Const DlpCmdWriteUserInfo As Byte = &H11
        Private Const DlpCmdReadSysInfo As Byte = &H12
        Private Const DlpCmdReadStorageInfo As Byte = &H15
        Private Const DlpCmdReadDBList As Byte = &H16
        Private Const DlpCmdOpenDB As Byte = &H17
        Private Const DlpCmdCreateDB As Byte = &H18
        Private Const DlpCmdCloseDB As Byte = &H19
        Private Const DlpCmdDeleteDB As Byte = &H1A
        Private Const DlpCmdWriteAppBlock As Byte = &H1C
        Private Const DlpCmdWriteSortBlock As Byte = &H1E
        Private Const DlpCmdReadRecord As Byte = &H20
        Private Const DlpCmdWriteRecord As Byte = &H21
        Private Const DlpCmdWriteResource As Byte = &H24
        Private Const DlpCmdReadOpenDBInfo As Byte = &H2B
        Private Const DlpCmdEndOfSync As Byte = &H2F
        Private Const DlpArgFirstId As Byte = &H20
        Private Const PalmRecordDeletedMask As Byte = &H80
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
            MemoOpenSent
            MemoInfoSent
            MemoReadSent
            MemoWriteSent
            MemoCloseSent
            MemoEndSent
            NoteDbListSent
            NoteOpenSent
            NoteInfoSent
            NoteReadSent
            NoteCloseSent
            NoteEndSent
            Done
            Failed
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
                        ResetMemoSync()
                        ResetNotePadSync()
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
                    NativeMusashi.palm_native_execute(PalmConfig.NativeAutoRunCyclesPerSlice)
                    PollNativeUart(False)
                    ServiceHotSyncHost()
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
            If pending > 0UI Then
                Dim buffer(CInt(Math.Min(256UI, pending)) - 1) As Byte
                Dim read = NativeMusashi.palm_native_uart_read_tx(buffer, CUInt(buffer.Length))
                For i = 0 To CInt(read) - 1
                    Dim value = buffer(i)
                    uartTxLogBuffer.Add(value)
                    If hotSyncRouteEnabled Then
                        uartSlpRxBuffer.Add(value)
                    End If
                Next
                If Not hotSyncRouteEnabled Then
                    uartSlpRxBuffer.Clear()
                Else
                    ProcessPalmSerialFrames()
                End If
            End If

            Dim nowTick = Environment.TickCount64
            If VerboseSerialLog AndAlso uartTxLogBuffer.Count > 0 AndAlso (forceLog OrElse nowTick - lastUartLogTick >= 250) Then
                Append($"UART TX {uartTxLogBuffer.Count}: {FormatBytes(uartTxLogBuffer)}")
                uartTxLogBuffer.Clear()
                lastUartLogTick = nowTick
            ElseIf Not VerboseSerialLog AndAlso uartTxLogBuffer.Count > 4096 Then
                uartTxLogBuffer.Clear()
                lastUartLogTick = nowTick
            End If
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
                    ResetMemoSync()
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
            ElseIf cmd = MemoExpectedResponseCommand() Then
                HandleMemoSyncResponse(payload, err)
                Return
            ElseIf cmd = NotePadExpectedResponseCommand() Then
                HandleNotePadSyncResponse(payload, err)
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

        Private Function MemoExpectedResponseCommand() As Byte
            Select Case installState
                Case HotSyncInstallState.MemoOpenSent
                    Return DlpCmdOpenDB
                Case HotSyncInstallState.MemoInfoSent
                    Return DlpCmdReadOpenDBInfo
                Case HotSyncInstallState.MemoReadSent
                    Return DlpCmdReadRecord
                Case HotSyncInstallState.MemoWriteSent
                    Return DlpCmdWriteRecord
                Case HotSyncInstallState.MemoCloseSent
                    Return DlpCmdCloseDB
                Case HotSyncInstallState.MemoEndSent
                    Return DlpCmdEndOfSync
                Case Else
                    Return 0
            End Select
        End Function

        Private Function NotePadExpectedResponseCommand() As Byte
            Select Case installState
                Case HotSyncInstallState.NoteDbListSent
                    Return DlpCmdReadDBList
                Case HotSyncInstallState.NoteOpenSent
                    Return DlpCmdOpenDB
                Case HotSyncInstallState.NoteInfoSent
                    Return DlpCmdReadOpenDBInfo
                Case HotSyncInstallState.NoteReadSent
                    Return DlpCmdReadRecord
                Case HotSyncInstallState.NoteCloseSent
                    Return DlpCmdCloseDB
                Case HotSyncInstallState.NoteEndSent
                    Return DlpCmdEndOfSync
                Case Else
                    Return 0
            End Select
        End Function

        Private Sub BeginHotSyncWork()
            If installState <> HotSyncInstallState.Idle Then Return
            If pendingInstall Is Nothing Then PrepareNextPendingInstall()
            If pendingInstall Is Nothing Then
                BeginMemoSync()
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
                        BeginMemoSync()
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

        Private Sub BeginMemoSync()
            ResetMemoSync()
            Append("HotSync MemoPad sync starting.")
            SendDlpOpenMemoDb()
        End Sub

        Private Sub ResetMemoSync()
            memoDbId = 0
            memoRecordCount = 0
            memoRecordIndex = 0
            memoWriteIndex = 0
            memoRecords.Clear()
            memoPendingWrites.Clear()
        End Sub

        Private Sub HandleMemoSyncResponse(payload As Byte(), err As Integer)
            Select Case installState
                Case HotSyncInstallState.MemoOpenSent
                    If err <> 0 Then
                        Append(If(err = 5, "MemoDB not found; nothing to sync.", $"MemoDB open failed ${err:X4}"))
                        BeginNotePadSync()
                        Return
                    End If
                    memoDbId = DecodeSingleByteArg(payload)
                    SendDlpReadOpenDbInfo()
                Case HotSyncInstallState.MemoInfoSent
                    If err <> 0 Then
                        Append($"MemoDB info failed ${err:X4}")
                        SendDlpCloseMemoDb()
                        Return
                    End If
                    memoRecordCount = DecodeMemoRecordCount(payload)
                    Append($"MemoDB records: {memoRecordCount}")
                    memoRecordIndex = 0
                    SendNextMemoRecord()
                Case HotSyncInstallState.MemoReadSent
                    If err <> 0 Then
                        Append($"Memo record {memoRecordIndex} read failed ${err:X4}")
                        SendDlpCloseMemoDb()
                        Return
                    End If
                    Dim record = DecodeMemoRecord(payload)
                    If record.RecordId <> 0UI AndAlso (record.Attributes And PalmRecordDeletedMask) = 0 Then
                        memoRecords.Add(record)
                    End If
                    memoRecordIndex += 1
                    SendNextMemoRecord()
                Case HotSyncInstallState.MemoWriteSent
                    If err <> 0 Then
                        Append($"Memo write {memoWriteIndex + 1}/{memoPendingWrites.Count} failed ${err:X4}")
                        SendDlpCloseMemoDb()
                        Return
                    End If
                    If memoWriteIndex < memoPendingWrites.Count Then
                        Dim written = memoPendingWrites(memoWriteIndex)
                        Dim dataOffset = DlpFirstArgDataOffset(payload)
                        If dataOffset >= 0 AndAlso payload.Length >= dataOffset + 4 Then written.RecordId = ReadU32(payload, dataOffset)
                    End If
                    memoWriteIndex += 1
                    SendNextMemoWrite()
                Case HotSyncInstallState.MemoCloseSent
                    If err <> 0 Then Append($"MemoDB close failed ${err:X4}")
                    SaveMemoTextFile()
                    BeginNotePadSync()
                Case HotSyncInstallState.MemoEndSent
                    If err <> 0 Then
                        Append($"MemoPad sync EndOfSync failed ${err:X4}")
                    Else
                        Append("HotSync MemoPad sync complete.")
                    End If
                    installState = HotSyncInstallState.Done
                    ResetMemoSync()
            End Select
        End Sub

        Private Sub SendDlpOpenMemoDb()
            Dim nameBytes = Encoding.ASCII.GetBytes("MemoDB" & ChrW(0))
            Dim arg(2 + nameBytes.Length - 1) As Byte
            arg(0) = 0
            arg(1) = &HD0
            Array.Copy(nameBytes, 0, arg, 2, nameBytes.Length)
            SendDlpRequestWithArg(DlpCmdOpenDB, arg, "DLP Open MemoDB")
            installState = HotSyncInstallState.MemoOpenSent
        End Sub

        Private Sub SendDlpReadOpenDbInfo()
            SendDlpRequestWithArg(DlpCmdReadOpenDBInfo, New Byte() {memoDbId}, "DLP ReadOpenDBInfo")
            installState = HotSyncInstallState.MemoInfoSent
        End Sub

        Private Sub SendNextMemoRecord()
            If memoRecordIndex >= memoRecordCount Then
                PrepareMemoWrites()
                SendNextMemoWrite()
                Return
            End If

            Dim arg(7) As Byte
            arg(0) = memoDbId
            arg(1) = 0
            WriteU16(arg, 2, CUShort(memoRecordIndex))
            WriteU16(arg, 4, 0US)
            WriteU16(arg, 6, &HFFFFUS)
            SendDlpRequestWithArg(DlpCmdReadRecord, CByte(DlpArgFirstId + 1), arg, $"DLP ReadMemoRecord {memoRecordIndex + 1}/{memoRecordCount}")
            installState = HotSyncInstallState.MemoReadSent
        End Sub

        Private Sub SendNextMemoWrite()
            If memoWriteIndex >= memoPendingWrites.Count Then
                SendDlpCloseMemoDb()
                Return
            End If

            Dim record = memoPendingWrites(memoWriteIndex)
            Dim textBytes = Encoding.ASCII.GetBytes(LimitPalmMemoText(record.Text) & ChrW(0))
            Dim arg(8 + textBytes.Length - 1) As Byte
            arg(0) = memoDbId
            arg(1) = &H80
            WriteU32(arg, 2, record.RecordId)
            arg(6) = CByte(record.Attributes And &H70)
            arg(7) = CByte(record.Category And &HF)
            Array.Copy(textBytes, 0, arg, 8, textBytes.Length)
            SendDlpRequestWithArg(DlpCmdWriteRecord, arg, $"DLP WriteMemo {memoWriteIndex + 1}/{memoPendingWrites.Count}")
            installState = HotSyncInstallState.MemoWriteSent
        End Sub

        Private Sub SendDlpCloseMemoDb()
            If memoDbId <> 0 Then
                SendDlpRequestWithArg(DlpCmdCloseDB, New Byte() {memoDbId}, "DLP Close MemoDB")
                installState = HotSyncInstallState.MemoCloseSent
            Else
                SendDlpEndOfSync()
                installState = HotSyncInstallState.MemoEndSent
            End If
        End Sub

        Private Function DecodeMemoRecordCount(payload As Byte()) As Integer
            Dim dataOffset = DlpFirstArgDataOffset(payload)
            If dataOffset < 0 OrElse payload.Length < dataOffset + 2 Then Return 0
            Return ReadU16(payload, dataOffset)
        End Function

        Private Function DecodeMemoRecord(payload As Byte()) As MemoRecordMirror
            Dim dataOffset = DlpFirstArgDataOffset(payload)
            If dataOffset < 0 OrElse payload.Length < dataOffset + 10 Then Return New MemoRecordMirror()

            Dim recordId = ReadU32(payload, dataOffset)
            Dim recSize = ReadU16(payload, dataOffset + 6)
            Dim attributes = payload(dataOffset + 8)
            Dim category = payload(dataOffset + 9)
            Dim textOffset = dataOffset + 10
            Dim textLength = Math.Min(recSize, payload.Length - textOffset)
            If textLength <= 0 Then Return New MemoRecordMirror With {.RecordId = recordId, .Attributes = attributes, .Category = category}
            If payload(textOffset + textLength - 1) = 0 Then textLength -= 1
            Return New MemoRecordMirror With {
                .RecordId = recordId,
                .Attributes = attributes,
                .Category = category,
                .Text = Encoding.ASCII.GetString(payload, textOffset, Math.Max(0, textLength))
            }
        End Function

        Private Function DlpFirstArgDataOffset(payload As Byte()) As Integer
            If payload.Length < 6 Then Return -1
            Dim argOffset = 4
            Dim argId = payload(argOffset)
            If (argId And &HC0) = &H80 Then
                If payload.Length < argOffset + 4 Then Return -1
                Return argOffset + 4
            End If
            Return argOffset + 2
        End Function

        Private Sub PrepareMemoWrites()
            memoPendingWrites.Clear()
            Dim memoDir = Path.Combine(hotSyncPath, "MemoPad")
            Dim manifest = LoadMemoManifest(memoDir)
            Dim seenFiles As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

            For i = 0 To memoRecords.Count - 1
                Dim record = memoRecords(i)
                Dim key = record.RecordId.ToString("X8", CultureInfo.InvariantCulture)
                Dim entry As MemoManifestEntry = Nothing
                If Not manifest.TryGetValue(key, entry) Then
                    entry = New MemoManifestEntry With {.RecordId = record.RecordId, .FileName = MakeMemoFileName(record, i)}
                    manifest(key) = entry
                End If

                record.FileName = SanitizeMemoFileName(entry.FileName)
                Dim filePath = Path.Combine(memoDir, record.FileName)
                seenFiles.Add(filePath)
                Dim palmHash = HashText(record.Text)
                Dim pcChanged = File.Exists(filePath) AndAlso HashText(File.ReadAllText(filePath, Encoding.ASCII)) <> entry.FileHash
                Dim palmChanged = entry.PalmHash.Length > 0 AndAlso palmHash <> entry.PalmHash

                If pcChanged AndAlso Not palmChanged Then
                    record.Text = NormalizeMemoTextForPalm(File.ReadAllText(filePath, Encoding.ASCII))
                    memoPendingWrites.Add(record)
                ElseIf pcChanged AndAlso palmChanged Then
                    record.ConflictText = record.Text
                    Append($"Memo conflict kept on PC and Palm: {record.FileName}")
                End If
            Next

            If Directory.Exists(memoDir) Then
                For Each filePath In Directory.EnumerateFiles(memoDir, "*.txt")
                    If filePath.EndsWith(".palm-conflict.txt", StringComparison.OrdinalIgnoreCase) Then Continue For
                    If seenFiles.Contains(filePath) Then Continue For
                    Dim fileName = Path.GetFileName(filePath)
                    Dim known = manifest.Values.Any(Function(e) String.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase))
                    If Not known Then
                        memoPendingWrites.Add(New MemoRecordMirror With {
                            .RecordId = 0UI,
                            .Attributes = 0,
                            .Category = 0,
                            .FileName = SanitizeMemoFileName(fileName),
                            .Text = NormalizeMemoTextForPalm(File.ReadAllText(filePath, Encoding.ASCII))
                        })
                    End If
                Next
            End If

            memoWriteIndex = 0
            If memoPendingWrites.Count > 0 Then Append($"MemoPad PC changes to write: {memoPendingWrites.Count}")
        End Sub

        Private Sub SaveMemoTextFile()
            Try
                Dim memoDir = Path.Combine(hotSyncPath, "MemoPad")
                Directory.CreateDirectory(memoDir)
                Dim oldManifest = LoadMemoManifest(memoDir)
                Dim manifest As New List(Of MemoManifestEntry)
                Dim currentFiles As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

                For i = 0 To memoRecords.Count - 1
                    Dim record = memoRecords(i)
                    If record.FileName.Length = 0 Then record.FileName = MakeMemoFileName(record, i)
                    Dim filePath = Path.Combine(memoDir, record.FileName)
                    currentFiles.Add(record.FileName)
                    If record.ConflictText.Length > 0 Then
                        Dim conflictPath = Path.Combine(memoDir, Path.GetFileNameWithoutExtension(record.FileName) & ".palm-conflict.txt")
                        File.WriteAllText(conflictPath, NormalizeMemoTextForPc(record.ConflictText), Encoding.ASCII)
                    Else
                        File.WriteAllText(filePath, NormalizeMemoTextForPc(record.Text), Encoding.ASCII)
                    End If
                    manifest.Add(New MemoManifestEntry With {
                        .RecordId = record.RecordId,
                        .FileName = record.FileName,
                        .FileHash = HashText(If(File.Exists(filePath), File.ReadAllText(filePath, Encoding.ASCII), record.Text)),
                        .PalmHash = HashText(record.Text)
                    })
                Next

                Dim createdRecords = memoPendingWrites.Where(Function(m) m.RecordId <> 0UI AndAlso Not memoRecords.Any(Function(r) r.RecordId = m.RecordId)).ToList()
                For Each createdRecord As MemoRecordMirror In createdRecords
                    If createdRecord.FileName.Length = 0 Then createdRecord.FileName = MakeMemoFileName(createdRecord, manifest.Count)
                    currentFiles.Add(createdRecord.FileName)
                    manifest.Add(New MemoManifestEntry With {
                        .RecordId = createdRecord.RecordId,
                        .FileName = createdRecord.FileName,
                        .FileHash = HashText(createdRecord.Text),
                        .PalmHash = HashText(createdRecord.Text)
                    })
                Next

                RemoveStaleMemoFiles(memoDir, oldManifest.Values, currentFiles)
                SaveMemoManifest(memoDir, manifest)
                Append($"MemoPad synced: {manifest.Count} memo(s) -> {memoDir}")
            Catch ex As Exception
                Append($"MemoPad save failed: {ex.Message}")
            End Try
        End Sub

        Private Sub RemoveStaleMemoFiles(memoDir As String, oldEntries As IEnumerable(Of MemoManifestEntry), currentFiles As HashSet(Of String))
            For Each oldEntry In oldEntries
                If oldEntry.FileName.Length = 0 OrElse currentFiles.Contains(oldEntry.FileName) Then Continue For

                Dim filePath = Path.Combine(memoDir, oldEntry.FileName)
                If File.Exists(filePath) Then File.Delete(filePath)
            Next
        End Sub

        Private Sub BeginNotePadSync()
            ResetNotePadSync()
            NoteTrace($"HotSync NotePad sync requested; profile={PalmConfig.ProfileName}")
            If PalmConfig.ActiveHardwareProfile <> PalmConfig.HardwareProfile.M100Experimental Then
                NoteTrace("NotePad sync skipped; active profile is not m100.")
                SendDlpEndOfSync()
                installState = HotSyncInstallState.NoteEndSent
                Return
            End If

            NoteTrace("HotSync NotePad sync starting.")
            SendDlpReadDbList(0)
        End Sub

        Private Sub ResetNotePadSync()
            noteDbId = 0
            noteDbNameIndex = 0
            noteDbListStartIndex = 0
            noteRecordCount = 0
            noteRecordIndex = 0
            noteDbNameCandidates.Clear()
            noteDbNameCandidates.AddRange(DefaultNotePadDbNames)
            dbListEntries.Clear()
            noteRecords.Clear()
        End Sub

        Private Sub HandleNotePadSyncResponse(payload As Byte(), err As Integer)
            Select Case installState
                Case HotSyncInstallState.NoteDbListSent
                    If err <> 0 Then
                        NoteTrace($"ReadDBList failed ${err:X4}; trying npadDB directly.")
                        SendDlpOpenNotePadDb()
                        Return
                    End If

                    Dim more = DecodeDbListResponse(payload)
                    If more Then
                        SendDlpReadDbList(noteDbListStartIndex)
                    Else
                        SaveDatabaseListDump()
                        PreferDiscoveredNotePadDbs()
                        SendDlpOpenNotePadDb()
                    End If
                Case HotSyncInstallState.NoteOpenSent
                    If err <> 0 Then
                        noteDbNameIndex += 1
                        If noteDbNameIndex < noteDbNameCandidates.Count Then
                            SendDlpOpenNotePadDb()
                        Else
                            NoteTrace("NotePad DB not found; nothing to sync.")
                            SendDlpEndOfSync()
                            installState = HotSyncInstallState.NoteEndSent
                        End If
                        Return
                    End If
                    noteDbId = DecodeSingleByteArg(payload)
                    SendDlpReadNotePadOpenDbInfo()
                Case HotSyncInstallState.NoteInfoSent
                    If err <> 0 Then
                        NoteTrace($"NotePad DB info failed ${err:X4}")
                        SendDlpCloseNotePadDb()
                        Return
                    End If
                    noteRecordCount = DecodeMemoRecordCount(payload)
                    NoteTrace($"NotePad records: {noteRecordCount}")
                    noteRecordIndex = 0
                    SendNextNotePadRecord()
                Case HotSyncInstallState.NoteReadSent
                    If err <> 0 Then
                        NoteTrace($"NotePad record {noteRecordIndex} read failed ${err:X4}")
                        SendDlpCloseNotePadDb()
                        Return
                    End If
                    Dim record = DecodeNotePadRecord(payload)
                    NoteTrace($"Read NotePad record id ${record.RecordId:X8}, attr ${record.Attributes:X2}, bytes {record.Data.Length}")
                    If record.RecordId <> 0UI AndAlso (record.Attributes And PalmRecordDeletedMask) = 0 Then
                        noteRecords.Add(record)
                    End If
                    noteRecordIndex += 1
                    SendNextNotePadRecord()
                Case HotSyncInstallState.NoteCloseSent
                    If err <> 0 Then NoteTrace($"NotePad DB close failed ${err:X4}")
                    SaveNotePadImages()
                    SendDlpEndOfSync()
                    installState = HotSyncInstallState.NoteEndSent
                Case HotSyncInstallState.NoteEndSent
                    If err <> 0 Then
                        NoteTrace($"NotePad sync EndOfSync failed ${err:X4}")
                    Else
                        NoteTrace("HotSync NotePad sync complete.")
                    End If
                    installState = HotSyncInstallState.Done
                    ResetNotePadSync()
                    ResetMemoSync()
            End Select
        End Sub

        Private Shared ReadOnly Property DefaultNotePadDbNames As String()
            Get
                ' m100 Note Pad stores drawings in npadDB, creator npad, type DATA.
                Return New String() {"npadDB"}
            End Get
        End Property

        Private Sub NoteTrace(message As String)
            Append(message)
        End Sub

        Private Sub SendDlpReadDbList(startIndex As Integer)
            Dim arg(3) As Byte
            arg(0) = &HE0
            arg(1) = 0
            WriteU16(arg, 2, CUShort(Math.Max(0, Math.Min(&HFFFF, startIndex))))
            SendDlpRequestWithArg(DlpCmdReadDBList, arg, $"DLP ReadDBList {startIndex}")
            installState = HotSyncInstallState.NoteDbListSent
        End Sub

        Private Sub SendDlpOpenNotePadDb()
            Dim dbName = noteDbNameCandidates(noteDbNameIndex)
            NoteTrace($"Opening NotePad DB '{dbName}' ({noteDbNameIndex + 1}/{noteDbNameCandidates.Count})")
            Dim nameBytes = Encoding.ASCII.GetBytes(dbName & ChrW(0))
            Dim arg(2 + nameBytes.Length - 1) As Byte
            arg(0) = 0
            arg(1) = &HD0
            Array.Copy(nameBytes, 0, arg, 2, nameBytes.Length)
            SendDlpRequestWithArg(DlpCmdOpenDB, arg, $"DLP Open NotePad {dbName}")
            installState = HotSyncInstallState.NoteOpenSent
        End Sub

        Private Sub SendDlpReadNotePadOpenDbInfo()
            SendDlpRequestWithArg(DlpCmdReadOpenDBInfo, New Byte() {noteDbId}, "DLP ReadNotePadOpenDBInfo")
            installState = HotSyncInstallState.NoteInfoSent
        End Sub

        Private Sub SendNextNotePadRecord()
            If noteRecordIndex >= noteRecordCount Then
                SendDlpCloseNotePadDb()
                Return
            End If

            Dim arg(7) As Byte
            arg(0) = noteDbId
            arg(1) = 0
            WriteU16(arg, 2, CUShort(noteRecordIndex))
            WriteU16(arg, 4, 0US)
            WriteU16(arg, 6, &HFFFFUS)
            NoteTrace($"Reading NotePad record {noteRecordIndex + 1}/{noteRecordCount}")
            SendDlpRequestWithArg(DlpCmdReadRecord, CByte(DlpArgFirstId + 1), arg, $"DLP ReadNotePadRecord {noteRecordIndex + 1}/{noteRecordCount}")
            installState = HotSyncInstallState.NoteReadSent
        End Sub

        Private Sub SendDlpCloseNotePadDb()
            If noteDbId <> 0 Then
                SendDlpRequestWithArg(DlpCmdCloseDB, New Byte() {noteDbId}, "DLP Close NotePad DB")
                installState = HotSyncInstallState.NoteCloseSent
            Else
                SendDlpEndOfSync()
                installState = HotSyncInstallState.NoteEndSent
            End If
        End Sub

        Private Function DecodeNotePadRecord(payload As Byte()) As NotePadRecordMirror
            Dim dataOffset = DlpFirstArgDataOffset(payload)
            If dataOffset < 0 OrElse payload.Length < dataOffset + 10 Then Return New NotePadRecordMirror()

            Dim recordId = ReadU32(payload, dataOffset)
            Dim recSize = ReadU16(payload, dataOffset + 6)
            Dim attributes = payload(dataOffset + 8)
            Dim category = payload(dataOffset + 9)
            Dim dataOffsetStart = dataOffset + 10
            Dim dataLength = Math.Min(recSize, payload.Length - dataOffsetStart)
            If dataLength < 0 Then dataLength = 0

            Dim bytes As Byte() = Array.Empty(Of Byte)()
            If dataLength > 0 Then ReDim bytes(dataLength - 1)
            If dataLength > 0 Then Array.Copy(payload, dataOffsetStart, bytes, 0, dataLength)
            Return New NotePadRecordMirror With {
                .RecordId = recordId,
                .Attributes = attributes,
                .Category = category,
                .Data = bytes
            }
        End Function

        Private Function DecodeDbListResponse(payload As Byte()) As Boolean
            Dim dataOffset = DlpFirstArgDataOffset(payload)
            If dataOffset < 0 OrElse payload.Length < dataOffset + 4 Then Return False

            Dim lastIndex = ReadU16(payload, dataOffset)
            Dim flags = payload(dataOffset + 2)
            Dim count = payload(dataOffset + 3)
            Dim cursor = dataOffset + 4
            For i = 0 To count - 1
                If cursor + 44 > payload.Length Then Exit For
                Dim totalSize = payload(cursor)
                If totalSize < 46 OrElse cursor + totalSize > payload.Length Then Exit For

                Dim entry As New DbListEntry With {
                    .MiscFlags = payload(cursor + 1),
                    .DbFlags = CUShort(ReadU16(payload, cursor + 2)),
                    .DbType = ReadU32(payload, cursor + 4),
                    .Creator = ReadU32(payload, cursor + 8),
                    .Version = CUShort(ReadU16(payload, cursor + 12)),
                    .ModNum = ReadU32(payload, cursor + 14),
                    .DbIndex = CUShort(ReadU16(payload, cursor + 42)),
                    .Name = ReadNullTerminatedAscii(payload, cursor + 44, totalSize - 44)
                }
                dbListEntries.Add(entry)
                cursor += totalSize
            Next

            noteDbListStartIndex = lastIndex + 1
            Return (flags And &H80) <> 0
        End Function

        Private Sub SaveDatabaseListDump()
            Try
                Directory.CreateDirectory(hotSyncPath)
                Dim dumpPath = Path.Combine(hotSyncPath, "DatabaseList.tsv")
                Dim sb As New StringBuilder()
                sb.AppendLine("index" & ControlChars.Tab & "name" & ControlChars.Tab & "type" & ControlChars.Tab & "creator" & ControlChars.Tab & "flags" & ControlChars.Tab & "misc" & ControlChars.Tab & "version" & ControlChars.Tab & "modNum")
                For Each entry In dbListEntries.OrderBy(Function(e) e.DbIndex).ThenBy(Function(e) e.Name, StringComparer.OrdinalIgnoreCase)
                    sb.Append(entry.DbIndex.ToString(CultureInfo.InvariantCulture))
                    sb.Append(ControlChars.Tab)
                    sb.Append(entry.Name)
                    sb.Append(ControlChars.Tab)
                    sb.Append(FourCc(entry.DbType))
                    sb.Append(ControlChars.Tab)
                    sb.Append(FourCc(entry.Creator))
                    sb.Append(ControlChars.Tab)
                    sb.Append("$" & entry.DbFlags.ToString("X4", CultureInfo.InvariantCulture))
                    sb.Append(ControlChars.Tab)
                    sb.Append("$" & entry.MiscFlags.ToString("X2", CultureInfo.InvariantCulture))
                    sb.Append(ControlChars.Tab)
                    sb.Append(entry.Version.ToString(CultureInfo.InvariantCulture))
                    sb.Append(ControlChars.Tab)
                    sb.AppendLine(entry.ModNum.ToString(CultureInfo.InvariantCulture))
                Next
                File.WriteAllText(dumpPath, sb.ToString(), Encoding.ASCII)
                NoteTrace($"Database list dumped: {dbListEntries.Count} DB(s) -> {dumpPath}")
            Catch ex As Exception
                NoteTrace($"Database list dump failed: {ex.Message}")
            End Try
        End Sub

        Private Sub PreferDiscoveredNotePadDbs()
            Dim discovered = dbListEntries.
                Where(Function(e) e.DbType = FourCcValue("DATA") AndAlso e.Creator = FourCcValue("npad")).
                Select(Function(e) e.Name).
                Where(Function(name) name.Length > 0).
                Distinct(StringComparer.OrdinalIgnoreCase).
                ToList()

            If discovered.Count = 0 Then Return

            Dim merged As New List(Of String)(discovered)
            For Each fallback In DefaultNotePadDbNames
                If Not merged.Any(Function(name) String.Equals(name, fallback, StringComparison.OrdinalIgnoreCase)) Then merged.Add(fallback)
            Next
            noteDbNameCandidates.Clear()
            noteDbNameCandidates.AddRange(merged)
            noteDbNameIndex = 0
            NoteTrace("NotePad DB candidate(s): " & String.Join(", ", discovered))
        End Sub

        Private Shared Function ReadNullTerminatedAscii(buffer As Byte(), offset As Integer, maxLength As Integer) As String
            Dim length = 0
            While length < maxLength AndAlso offset + length < buffer.Length AndAlso buffer(offset + length) <> 0
                length += 1
            End While
            Return Encoding.ASCII.GetString(buffer, offset, length)
        End Function

        Private Sub SaveNotePadImages()
            Try
                Dim noteDir = Path.Combine(hotSyncPath, "NotePad")
                Directory.CreateDirectory(noteDir)
                Dim currentFiles As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

                For i = 0 To noteRecords.Count - 1
                    Dim record = noteRecords(i)
                    Dim bitmap = DecodeNotePadBitmap(record.Data)
                    Dim baseName = $"{record.RecordId:X8} - Note {i + 1}"
                    If bitmap.Pixels IsNot Nothing AndAlso bitmap.Pixels.Length > 0 Then
                        Dim fileName = SanitizeBmpFileName(baseName & ".bmp")
                        WriteOneBitBmp(Path.Combine(noteDir, fileName), bitmap.Width, bitmap.Height, bitmap.RowBytes, bitmap.Pixels)
                        currentFiles.Add(fileName)
                        Dim rawName = SanitizeBmpFileName(baseName & ".bin")
                        File.WriteAllBytes(Path.Combine(noteDir, rawName), record.Data)
                        currentFiles.Add(rawName)
                    Else
                        Dim rawName = SanitizeBmpFileName(baseName & ".bin")
                        File.WriteAllBytes(Path.Combine(noteDir, rawName), record.Data)
                        currentFiles.Add(rawName)
                        NoteTrace($"NotePad record {i + 1} saved raw; bitmap format not recognized.")
                    End If
                Next

                For Each filePath In Directory.EnumerateFiles(noteDir)
                    Dim name = Path.GetFileName(filePath)
                    If Not currentFiles.Contains(name) Then File.Delete(filePath)
                Next

                NoteTrace($"NotePad synced: {noteRecords.Count} note(s) -> {noteDir}")
            Catch ex As Exception
                NoteTrace($"NotePad save failed: {ex}")
            End Try
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

        Private Function LoadMemoManifest(memoDir As String) As Dictionary(Of String, MemoManifestEntry)
            Dim result As New Dictionary(Of String, MemoManifestEntry)(StringComparer.OrdinalIgnoreCase)
            Dim manifestPath = Path.Combine(memoDir, "manifest.tsv")
            If Not File.Exists(manifestPath) Then Return result

            For Each line In File.ReadAllLines(manifestPath, Encoding.ASCII)
                If line.Length = 0 OrElse line.StartsWith("#", StringComparison.Ordinal) Then Continue For
                Dim parts = line.Split(ControlChars.Tab)
                If parts.Length < 4 Then Continue For
                Dim recordId As UInteger
                If Not UInteger.TryParse(parts(0), NumberStyles.HexNumber, CultureInfo.InvariantCulture, recordId) Then Continue For
                Dim entry = New MemoManifestEntry With {
                    .RecordId = recordId,
                    .FileName = SanitizeMemoFileName(parts(1)),
                    .FileHash = parts(2),
                    .PalmHash = parts(3)
                }
                result(recordId.ToString("X8", CultureInfo.InvariantCulture)) = entry
            Next

            Return result
        End Function

        Private Sub SaveMemoManifest(memoDir As String, entries As IEnumerable(Of MemoManifestEntry))
            Dim sb As New StringBuilder()
            sb.AppendLine("# recordId" & ControlChars.Tab & "file" & ControlChars.Tab & "fileHash" & ControlChars.Tab & "palmHash")
            For Each entry In entries.OrderBy(Function(e) e.FileName, StringComparer.OrdinalIgnoreCase)
                sb.Append(entry.RecordId.ToString("X8", CultureInfo.InvariantCulture))
                sb.Append(ControlChars.Tab)
                sb.Append(entry.FileName)
                sb.Append(ControlChars.Tab)
                sb.Append(entry.FileHash)
                sb.Append(ControlChars.Tab)
                sb.AppendLine(entry.PalmHash)
            Next
            File.WriteAllText(Path.Combine(memoDir, "manifest.tsv"), sb.ToString(), Encoding.ASCII)
        End Sub

        Private Shared Function MakeMemoFileName(record As MemoRecordMirror, index As Integer) As String
            Dim title = NormalizeMemoTextForPalm(record.Text).Split({vbLf}, StringSplitOptions.None)(0).Trim()
            If title.Length = 0 Then title = $"Memo {index + 1}"
            If title.Length > 48 Then title = title.Substring(0, 48).Trim()
            Return SanitizeMemoFileName($"{record.RecordId:X8} - {title}.txt")
        End Function

        Private Shared Function SanitizeMemoFileName(fileName As String) As String
            Dim invalid = Path.GetInvalidFileNameChars()
            Dim sb As New StringBuilder(fileName.Length)
            For Each ch In fileName
                sb.Append(If(invalid.Contains(ch), "_"c, ch))
            Next
            Dim sanitized = sb.ToString().Trim()
            If sanitized.Length = 0 Then sanitized = "memo.txt"
            If Not sanitized.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) Then sanitized &= ".txt"
            Return sanitized
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

        Private Shared Function DecodeNotePadBitmap(data As Byte()) As NotePadBitmap
            Dim m100Note = FindM100NotePadBitmap(data)
            If m100Note.Pixels IsNot Nothing AndAlso m100Note.Pixels.Length > 0 Then Return m100Note

            Dim header = FindPalmBitmapHeader(data)
            If header.Pixels IsNot Nothing AndAlso header.Pixels.Length > 0 Then Return header

            Const width = 160
            Const height = 160
            Const rowBytes = width \ 8
            Dim needed = rowBytes * height
            If data.Length >= needed Then
                Dim pixels(needed - 1) As Byte
                Array.Copy(data, data.Length - needed, pixels, 0, needed)
                Return New NotePadBitmap With {.Width = width, .Height = height, .RowBytes = rowBytes, .Pixels = pixels}
            End If

            Return New NotePadBitmap()
        End Function

        Private Shared Function FindM100NotePadBitmap(data As Byte()) As NotePadBitmap
            Const titleStart = &H1E
            If data.Length < titleStart + 24 Then Return New NotePadBitmap()

            Dim titleEnd = titleStart
            While titleEnd < data.Length AndAlso data(titleEnd) <> 0
                titleEnd += 1
            End While

            Dim headerOffset = titleEnd + 1
            If (headerOffset And 1) <> 0 Then headerOffset += 1
            If headerOffset + 24 > data.Length Then Return New NotePadBitmap()

            Dim width = CInt(ReadU32(data, headerOffset + 4))
            Dim height = CInt(ReadU32(data, headerOffset + 8))
            Dim depth = CInt(ReadU32(data, headerOffset + 12))
            Dim rleLength = CInt(Math.Min(ReadU32(data, headerOffset + 20), CUInt(data.Length - (headerOffset + 24))))

            If width < 80 OrElse width > 200 Then Return New NotePadBitmap()
            If height < 80 OrElse height > 512 Then Return New NotePadBitmap()
            If depth <> 1 Then Return New NotePadBitmap()
            If rleLength < 2 Then Return New NotePadBitmap()

            Dim rowBytes = ((width + 31) \ 32) * 4
            Dim pixelBytes = rowBytes * height
            Dim pixels(pixelBytes - 1) As Byte
            Dim sourceOffset = headerOffset + 24
            Dim sourceEnd = Math.Min(data.Length, sourceOffset + rleLength)
            Dim outOffset = 0

            While sourceOffset + 1 < sourceEnd AndAlso outOffset < pixelBytes
                Dim count = data(sourceOffset)
                Dim value = data(sourceOffset + 1)
                sourceOffset += 2
                Dim writeCount = Math.Min(count, pixelBytes - outOffset)
                If writeCount > 0 Then
                    Array.Fill(pixels, value, outOffset, writeCount)
                    outOffset += writeCount
                End If
            End While

            If outOffset <> pixelBytes Then Return New NotePadBitmap()
            Return New NotePadBitmap With {.Width = width, .Height = height, .RowBytes = rowBytes, .Pixels = pixels}
        End Function

        Private Shared Function FindPalmBitmapHeader(data As Byte()) As NotePadBitmap
            For offset = 0 To Math.Max(-1, data.Length - 16)
                Dim width = ReadU16(data, offset)
                Dim height = ReadU16(data, offset + 2)
                Dim rowBytes = ReadU16(data, offset + 4) And &H3FFF
                If width < 120 OrElse width > 200 Then Continue For
                If height < 100 OrElse height > 240 Then Continue For
                If rowBytes < (width + 7) \ 8 OrElse rowBytes > 64 Then Continue For

                For headerBytes = 16 To 24 Step 4
                    Dim pixelOffset = offset + headerBytes
                    Dim pixelBytes = rowBytes * height
                    If pixelOffset + pixelBytes <= data.Length Then
                        Dim pixels(pixelBytes - 1) As Byte
                        Array.Copy(data, pixelOffset, pixels, 0, pixelBytes)
                        Return New NotePadBitmap With {.Width = width, .Height = height, .RowBytes = rowBytes, .Pixels = pixels}
                    End If
                Next
            Next

            Return New NotePadBitmap()
        End Function

        Private Shared Sub WriteOneBitBmp(path As String, width As Integer, height As Integer, sourceRowBytes As Integer, pixels As Byte())
            Dim bmpRowBytes = ((width + 31) \ 32) * 4
            Dim pixelDataSize = bmpRowBytes * height
            Dim imageOffset = 14 + 40 + 8
            Dim fileSize = imageOffset + pixelDataSize

            Using fs As New FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read)
                Using bw As New BinaryWriter(fs)
                    bw.Write(CByte(AscW("B"c)))
                    bw.Write(CByte(AscW("M"c)))
                    bw.Write(fileSize)
                    bw.Write(0US)
                    bw.Write(0US)
                    bw.Write(imageOffset)
                    bw.Write(40)
                    bw.Write(width)
                    bw.Write(height)
                    bw.Write(1US)
                    bw.Write(1US)
                    bw.Write(0)
                    bw.Write(pixelDataSize)
                    bw.Write(0)
                    bw.Write(0)
                    bw.Write(2)
                    bw.Write(2)
                    bw.Write(CByte(255))
                    bw.Write(CByte(255))
                    bw.Write(CByte(255))
                    bw.Write(CByte(0))
                    bw.Write(CByte(0))
                    bw.Write(CByte(0))
                    bw.Write(CByte(0))
                    bw.Write(CByte(0))

                    Dim row(bmpRowBytes - 1) As Byte
                    For y = height - 1 To 0 Step -1
                        Array.Clear(row, 0, row.Length)
                        Dim sourceOffset = y * sourceRowBytes
                        Dim copyBytes = Math.Min(sourceRowBytes, bmpRowBytes)
                        If sourceOffset + copyBytes <= pixels.Length Then Array.Copy(pixels, sourceOffset, row, 0, copyBytes)
                        bw.Write(row)
                    Next
                End Using
            End Using
        End Sub

        Private Shared Function NormalizeMemoTextForPalm(text As String) As String
            Return text.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
        End Function

        Private Shared Function NormalizeMemoTextForPc(text As String) As String
            Return NormalizeMemoTextForPalm(text).Replace(vbLf, vbCrLf)
        End Function

        Private Shared Function LimitPalmMemoText(text As String) As String
            Dim normalized = NormalizeMemoTextForPalm(text)
            Dim bytes = Encoding.ASCII.GetBytes(normalized)
            If bytes.Length < PalmMemoMaxBytes Then Return normalized

            Dim limited = Encoding.ASCII.GetString(bytes, 0, PalmMemoMaxBytes - 1)
            Return limited.TrimEnd(ControlChars.NullChar)
        End Function

        Private Shared Function HashText(text As String) As String
            Dim bytes = Encoding.ASCII.GetBytes(NormalizeMemoTextForPalm(text))
            Dim hash As UInteger = &H811C9DC5UI
            For Each value In bytes
                hash = hash Xor value
                hash = CUInt((CULng(hash) * &H1000193UL) And &HFFFFFFFFUL)
            Next
            Return hash.ToString("X8", CultureInfo.InvariantCulture)
        End Function

        Private Shared Function FourCc(value As UInteger) As String
            Dim chars = {
                CByte((value >> 24) And &HFFUI),
                CByte((value >> 16) And &HFFUI),
                CByte((value >> 8) And &HFFUI),
                CByte(value And &HFFUI)
            }
            Return Encoding.ASCII.GetString(chars)
        End Function

        Private Shared Function FourCcValue(text As String) As UInteger
            Dim bytes = Encoding.ASCII.GetBytes(text.PadRight(4).Substring(0, 4))
            Return (CUInt(bytes(0)) << 24) Or (CUInt(bytes(1)) << 16) Or (CUInt(bytes(2)) << 8) Or bytes(3)
        End Function

        Private NotInheritable Class MemoRecordMirror
            Public Property RecordId As UInteger
            Public Property Attributes As Byte
            Public Property Category As Byte
            Public Property Text As String = ""
            Public Property FileName As String = ""
            Public Property ConflictText As String = ""
        End Class

        Private NotInheritable Class MemoManifestEntry
            Public Property RecordId As UInteger
            Public Property FileName As String = ""
            Public Property FileHash As String = ""
            Public Property PalmHash As String = ""
        End Class

        Private NotInheritable Class DbListEntry
            Public Property Name As String = ""
            Public Property MiscFlags As Byte
            Public Property DbFlags As UShort
            Public Property DbType As UInteger
            Public Property Creator As UInteger
            Public Property Version As UShort
            Public Property ModNum As UInteger
            Public Property DbIndex As UShort
        End Class

        Private Structure NotePadBitmap
            Public Width As Integer
            Public Height As Integer
            Public RowBytes As Integer
            Public Pixels As Byte()
        End Structure

        Private NotInheritable Class NotePadRecordMirror
            Public Property RecordId As UInteger
            Public Property Attributes As Byte
            Public Property Category As Byte
            Public Property Data As Byte() = Array.Empty(Of Byte)()
        End Class

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
