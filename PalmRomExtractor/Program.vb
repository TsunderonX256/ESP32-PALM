Imports System.Globalization
Imports System.IO
Imports System.Text

Module Program
    Private Const PalmEpochYear As Integer = 1904
    Private Const DatabaseHeaderSize As Integer = 78
    Private Const ResourceEntrySize As Integer = 10
    Private Const RecordEntrySize As Integer = 8
    Private Const ResourceDatabaseAttribute As UShort = &H1US
    Private Const ReadOnlyDatabaseAttribute As UShort = &H2US
    Private Const OpenDatabaseAttribute As UShort = &H8000US

    Private NotInheritable Class PalmDatabase
        Public Property StartOffset As Integer
        Public Property BoundaryOffset As Integer
        Public Property Length As Integer
        Public Property Name As String = ""
        Public Property Attributes As UShort
        Public Property Version As UShort
        Public Property Created As DateTimeOffset?
        Public Property Modified As DateTimeOffset?
        Public Property TypeCode As String = ""
        Public Property CreatorCode As String = ""
        Public Property EntryCount As UShort
        Public Property EntryOffsets As List(Of Integer) = New List(Of Integer)()
        Public Property EntryFileOffsets As List(Of Integer) = New List(Of Integer)()
        Public Property KnownFileOffsets As List(Of Integer) = New List(Of Integer)()
        Public Property OffsetMode As String = ""

        Public ReadOnly Property IsResourceDatabase As Boolean
            Get
                Return (Attributes And ResourceDatabaseAttribute) <> 0
            End Get
        End Property

        Public ReadOnly Property IsApplication As Boolean
            Get
                Return IsResourceDatabase AndAlso TypeCode = "appl"
            End Get
        End Property
    End Class

    Function Main(args As String()) As Integer
        Console.OutputEncoding = Encoding.UTF8

        If args.Length = 0 OrElse IsHelp(args(0)) Then
            PrintUsage()
            Return If(args.Length = 0, 2, 0)
        End If

        Dim command = args(0).ToLowerInvariant()
        Select Case command
            Case "list"
                If args.Length < 2 Then
                    PrintUsage()
                    Return 2
                End If

                Return ListApplications(args)
            Case "export"
                If args.Length < 3 Then
                    PrintUsage()
                    Return 2
                End If

                Return ExportApplications(args)
            Case Else
                Console.Error.WriteLine($"unknown command: {args(0)}")
                PrintUsage()
                Return 2
        End Select
    End Function

    Private Function ListApplications(args As String()) As Integer
        Dim romPath = Path.GetFullPath(args(1))
        Dim romBase = ParseOptionalRomBase(args, 2)
        Dim databases = ReadDatabases(romPath, romBase)
        Dim apps = databases.Where(Function(db) db.IsApplication).OrderBy(Function(db) db.Name, StringComparer.OrdinalIgnoreCase).ToList()

        Console.WriteLine($"ROM: {romPath}")
        Console.WriteLine($"Applications: {apps.Count}")
        Console.WriteLine()
        Console.WriteLine($"{"Offset",-10} {"Size",8} {"Creator",-7} {"Recs",5} {"Name"}")
        Console.WriteLine(New String("-"c, 64))

        For Each app In apps
            Console.WriteLine($"0x{app.StartOffset:X6} {app.Length,8} {app.CreatorCode,-7} {app.EntryCount,5} {app.Name}")
        Next

        Return 0
    End Function

    Private Function ExportApplications(args As String()) As Integer
        Dim romPath = Path.GetFullPath(args(1))
        Dim outputDirectory = Path.GetFullPath(args(2))
        Dim romBase = ParseOptionalRomBase(args, 3)
        Dim nameFilter = GetOptionValue(args, "--name")
        Dim creatorFilter = GetOptionValue(args, "--creator")
        Dim includeRelated = HasOption(args, "--include-related")
        Dim exportAll = HasOption(args, "--all") OrElse (String.IsNullOrWhiteSpace(nameFilter) AndAlso String.IsNullOrWhiteSpace(creatorFilter))

        Directory.CreateDirectory(outputDirectory)

        Dim rom = File.ReadAllBytes(romPath)
        Dim databases = FindDatabases(rom, romBase)
        Dim apps = databases.Where(Function(db) db.IsApplication OrElse (includeRelated AndAlso db.IsResourceDatabase AndAlso db.TypeCode = "ovly"))

        If Not exportAll Then
            If Not String.IsNullOrWhiteSpace(nameFilter) Then
                apps = apps.Where(Function(db) db.Name.Equals(nameFilter, StringComparison.OrdinalIgnoreCase))
            End If

            If Not String.IsNullOrWhiteSpace(creatorFilter) Then
                apps = apps.Where(Function(db) db.CreatorCode.Equals(creatorFilter, StringComparison.OrdinalIgnoreCase))
            End If
        End If

        Dim selectedApps = apps.OrderBy(Function(db) db.Name, StringComparer.OrdinalIgnoreCase).ToList()
        If selectedApps.Count = 0 Then
            Console.Error.WriteLine("no matching ROM applications found")
            Return 1
        End If

        For Each app In selectedApps
            Dim outputPath = Path.Combine(outputDirectory, MakeSafeFileName($"{app.Name}-{app.CreatorCode}.prc"))
            ExportDatabase(rom, app, outputPath)
            Console.WriteLine($"{app.Name} ({app.CreatorCode}) -> {outputPath}")
        Next

        Console.WriteLine()
        Console.WriteLine($"Exported {selectedApps.Count} database(s).")
        Return 0
    End Function

    Private Function ReadDatabases(romPath As String, romBase As UInteger?) As List(Of PalmDatabase)
        If Not File.Exists(romPath) Then
            Throw New FileNotFoundException("ROM dump not found", romPath)
        End If

        Return FindDatabases(File.ReadAllBytes(romPath), romBase)
    End Function

    Private Function FindDatabases(rom As Byte(), romBase As UInteger?) As List(Of PalmDatabase)
        Dim databases As New List(Of PalmDatabase)()
        Dim effectiveRomBase = If(romBase, GuessRomBase(rom))

        For offset = 0 To rom.Length - DatabaseHeaderSize
            Dim candidate = TryReadDatabase(rom, offset, effectiveRomBase)
            If candidate IsNot Nothing Then
                databases.Add(candidate)
            End If
        Next

        databases = databases.OrderBy(Function(db) db.StartOffset).ToList()

        Dim knownFileOffsets = databases.
            SelectMany(Function(db) db.EntryFileOffsets).
            Distinct().
            OrderBy(Function(offset) offset).
            ToList()

        For index = 0 To databases.Count - 1
            Dim db = databases(index)
            db.BoundaryOffset = If(index + 1 < databases.Count, databases(index + 1).StartOffset, rom.Length)
            db.KnownFileOffsets = knownFileOffsets
            db.Length = EstimateExportLength(rom.Length, db)
        Next

        Return databases
    End Function

    Private Function TryReadDatabase(rom As Byte(), startOffset As Integer, romBase As UInteger?) As PalmDatabase
        Dim name = ReadNullTerminatedAscii(rom, startOffset, 32)
        If String.IsNullOrWhiteSpace(name) OrElse Not IsMostlyPrintable(name) Then
            Return Nothing
        End If

        Dim attributes = ReadUInt16BE(rom, startOffset + 32)
        Dim version = ReadUInt16BE(rom, startOffset + 34)
        Dim typeCode = ReadAscii(rom, startOffset + 60, 4)
        Dim creatorCode = ReadAscii(rom, startOffset + 64, 4)
        Dim nextRecordListId = ReadUInt32BE(rom, startOffset + 72)
        Dim entryCount = ReadUInt16BE(rom, startOffset + 76)
        Dim isResourceDb = (attributes And ResourceDatabaseAttribute) <> 0
        Dim entrySize = If(isResourceDb, ResourceEntrySize, RecordEntrySize)
        Dim entriesEnd = DatabaseHeaderSize + CInt(entryCount) * entrySize

        If entryCount = 0US OrElse entryCount > 4096US Then
            Return Nothing
        End If

        If startOffset + entriesEnd > rom.Length Then
            Return Nothing
        End If

        If nextRecordListId <> 0UI Then
            Return Nothing
        End If

        If Not IsFourCc(typeCode) OrElse Not IsFourCc(creatorCode) Then
            Return Nothing
        End If

        Dim rawOffsets As New List(Of UInteger)()
        For i = 0 To CInt(entryCount) - 1
            Dim entryOffset = startOffset + DatabaseHeaderSize + i * entrySize
            Dim rawDataOffset = If(isResourceDb, ReadUInt32BE(rom, entryOffset + 6), ReadUInt32BE(rom, entryOffset))
            rawOffsets.Add(rawDataOffset)
        Next

        Dim normalized = NormalizeOffsets(rawOffsets, startOffset, entriesEnd, rom.Length, romBase)
        If normalized Is Nothing Then
            Return Nothing
        End If

        Dim entryFileOffsets = normalized.Select(Function(item) item.FileOffset).ToList()
        Dim entryListEnd = startOffset + entriesEnd
        If entryFileOffsets.Any(Function(fileOffset) fileOffset >= startOffset AndAlso fileOffset < entryListEnd) Then
            Return Nothing
        End If

        Dim db As New PalmDatabase With {
            .StartOffset = startOffset,
            .Name = name,
            .Attributes = attributes,
            .Version = version,
            .Created = PalmDate(ReadUInt32BE(rom, startOffset + 36)),
            .Modified = PalmDate(ReadUInt32BE(rom, startOffset + 40)),
            .TypeCode = typeCode,
            .CreatorCode = creatorCode,
            .EntryCount = entryCount,
            .EntryOffsets = normalized.Select(Function(item) item.DatabaseOffset).ToList(),
            .EntryFileOffsets = entryFileOffsets,
            .OffsetMode = normalized(0).Mode
        }

        Return db
    End Function

    Private Function NormalizeOffsets(rawOffsets As List(Of UInteger), startOffset As Integer, entriesEnd As Integer, romLength As Integer, romBase As UInteger?) As List(Of (DatabaseOffset As Integer, FileOffset As Integer, Mode As String))
        Dim relative = TryNormalizeOffsets(rawOffsets, Function(raw) CLng(raw), Function(raw) CLng(startOffset) + raw, romLength, "relative")
        If relative IsNot Nothing Then
            Return relative
        End If

        Dim fileAbsolute = TryNormalizeOffsets(rawOffsets, Function(raw) CLng(raw) - startOffset, Function(raw) CLng(raw), romLength, "file-absolute")
        If fileAbsolute IsNot Nothing Then
            Return fileAbsolute
        End If

        If romBase.HasValue Then
            Dim baseValue = CLng(romBase.Value)
            Dim romAbsolute = TryNormalizeOffsets(rawOffsets, Function(raw) CLng(raw) - baseValue - startOffset, Function(raw) CLng(raw) - baseValue, romLength, "rom-base")
            If romAbsolute IsNot Nothing Then
                Return romAbsolute
            End If
        End If

        Dim inferredRomBase = InferRomBase(rawOffsets, romLength)
        If inferredRomBase.HasValue Then
            Dim baseValue = CLng(inferredRomBase.Value)
            Dim inferred = TryNormalizeOffsets(rawOffsets, Function(raw) CLng(raw) - baseValue - startOffset, Function(raw) CLng(raw) - baseValue, romLength, $"rom-base:0x{inferredRomBase.Value:X8}")
            If inferred IsNot Nothing Then
                Return inferred
            End If
        End If

        Return Nothing
    End Function

    Private Function InferRomBase(rawOffsets As List(Of UInteger), romLength As Integer) As UInteger?
        Dim minimumRaw = rawOffsets.Min()
        Dim candidate = minimumRaw And &HFFF00000UI

        If candidate = 0UI Then
            Return Nothing
        End If

        If rawOffsets.All(Function(raw) raw >= candidate AndAlso CLng(raw) - candidate < romLength) Then
            Return candidate
        End If

        Return Nothing
    End Function

    Private Function GuessRomBase(rom As Byte()) As UInteger?
        If rom.Length < 92 Then
            Return Nothing
        End If

        Dim ramList = ReadUInt32BE(rom, 88)
        If ramList < &H200UI Then
            Return Nothing
        End If

        Dim guessedBase = ramList - &H200UI
        If guessedBase = 0UI Then
            Return Nothing
        End If

        Return guessedBase
    End Function

    Private Function TryNormalizeOffsets(rawOffsets As List(Of UInteger), toDatabaseOffset As Func(Of UInteger, Long), toFileOffset As Func(Of UInteger, Long), romLength As Integer, mode As String) As List(Of (DatabaseOffset As Integer, FileOffset As Integer, Mode As String))
        Dim normalized As New List(Of (DatabaseOffset As Integer, FileOffset As Integer, Mode As String))()

        For Each raw In rawOffsets
            Dim databaseOffset = toDatabaseOffset(raw)
            Dim fileOffset = toFileOffset(raw)
            If fileOffset < 0 OrElse fileOffset >= romLength Then
                Return Nothing
            End If

            normalized.Add((CInt(databaseOffset), CInt(fileOffset), mode))
        Next

        Return normalized
    End Function

    Private Sub ExportDatabase(rom As Byte(), db As PalmDatabase, outputPath As String)
        Dim entrySize = If(db.IsResourceDatabase, ResourceEntrySize, RecordEntrySize)
        Dim headerLength = DatabaseHeaderSize + CInt(db.EntryCount) * entrySize
        Dim entryRanges = GetEntryRanges(rom.Length, db)
        Dim outputLength = headerLength + entryRanges.Sum(Function(range) range.Length)
        Dim output(outputLength - 1) As Byte

        Buffer.BlockCopy(rom, db.StartOffset, output, 0, headerLength)
        SanitizeInstallHeader(output)

        Dim writeOffset = headerLength
        For i = 0 To entryRanges.Count - 1
            Dim entryOffset = DatabaseHeaderSize + i * entrySize + If(db.IsResourceDatabase, 6, 0)
            WriteUInt32BE(output, entryOffset, CUInt(writeOffset))

            Dim range = entryRanges(i)
            Buffer.BlockCopy(rom, range.Start, output, writeOffset, range.Length)
            writeOffset += range.Length
        Next

        File.WriteAllBytes(outputPath, output)
    End Sub

    Private Sub SanitizeInstallHeader(output As Byte())
        Dim attributes = ReadUInt16BE(output, 32)
        attributes = CUShort(attributes And Not ReadOnlyDatabaseAttribute)
        attributes = CUShort(attributes And Not OpenDatabaseAttribute)
        WriteUInt16BE(output, 32, attributes)
        WriteUInt32BE(output, 72, 0UI)
    End Sub

    Private Function EstimateExportLength(romLength As Integer, db As PalmDatabase) As Integer
        Dim entrySize = If(db.IsResourceDatabase, ResourceEntrySize, RecordEntrySize)
        Dim headerLength = DatabaseHeaderSize + CInt(db.EntryCount) * entrySize
        Return headerLength + GetEntryRanges(romLength, db).Sum(Function(range) range.Length)
    End Function

    Private Function GetEntryRanges(romLength As Integer, db As PalmDatabase) As List(Of (Start As Integer, Length As Integer))
        Dim sortedOffsets = db.EntryFileOffsets.Distinct().OrderBy(Function(offset) offset).ToList()
        Dim headerStart = db.StartOffset
        Dim rangesByStart As New Dictionary(Of Integer, Integer)()

        For i = 0 To sortedOffsets.Count - 1
            Dim current = sortedOffsets(i)
            Dim nextOffset As Integer

            Dim nextKnownOffset = db.KnownFileOffsets.FirstOrDefault(Function(offset) offset > current)
            If nextKnownOffset > 0 Then
                nextOffset = nextKnownOffset
            ElseIf current < headerStart Then
                nextOffset = headerStart
            Else
                nextOffset = If(db.BoundaryOffset > current, db.BoundaryOffset, romLength)
            End If

            If nextOffset <= current Then
                Throw New InvalidDataException($"invalid resource range for {db.Name}")
            End If

            rangesByStart(current) = nextOffset - current
        Next

        Return db.EntryFileOffsets.Select(Function(offset) (Start:=offset, Length:=rangesByStart(offset))).ToList()
    End Function

    Private Function ReadUInt16BE(data As Byte(), offset As Integer) As UShort
        Return CUShort((CUInt(data(offset)) << 8) Or data(offset + 1))
    End Function

    Private Function ReadUInt32BE(data As Byte(), offset As Integer) As UInteger
        Return (CUInt(data(offset)) << 24) Or (CUInt(data(offset + 1)) << 16) Or (CUInt(data(offset + 2)) << 8) Or data(offset + 3)
    End Function

    Private Sub WriteUInt16BE(data As Byte(), offset As Integer, value As UShort)
        data(offset) = CByte((value >> 8) And &HFFUS)
        data(offset + 1) = CByte(value And &HFFUS)
    End Sub

    Private Sub WriteUInt32BE(data As Byte(), offset As Integer, value As UInteger)
        data(offset) = CByte((value >> 24) And &HFFUI)
        data(offset + 1) = CByte((value >> 16) And &HFFUI)
        data(offset + 2) = CByte((value >> 8) And &HFFUI)
        data(offset + 3) = CByte(value And &HFFUI)
    End Sub

    Private Function ReadAscii(data As Byte(), offset As Integer, count As Integer) As String
        Return Encoding.ASCII.GetString(data, offset, count)
    End Function

    Private Function ReadNullTerminatedAscii(data As Byte(), offset As Integer, maximumLength As Integer) As String
        Dim length = 0
        While length < maximumLength AndAlso data(offset + length) <> 0
            length += 1
        End While

        Return Encoding.ASCII.GetString(data, offset, length).Trim()
    End Function

    Private Function IsFourCc(value As String) As Boolean
        Return value.Length = 4 AndAlso value.All(Function(ch) ch >= " "c AndAlso ch <= "~"c)
    End Function

    Private Function IsMostlyPrintable(value As String) As Boolean
        Dim printable = value.Count(Function(ch) ch >= " "c AndAlso ch <= "~"c)
        Return printable = value.Length
    End Function

    Private Function PalmDate(seconds As UInteger) As DateTimeOffset?
        If seconds = 0UI Then
            Return Nothing
        End If

        Try
            Return New DateTimeOffset(PalmEpochYear, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(seconds)
        Catch ex As ArgumentOutOfRangeException
            Return Nothing
        End Try
    End Function

    Private Function MakeSafeFileName(value As String) As String
        Dim invalid = Path.GetInvalidFileNameChars()
        Dim chars = value.Select(Function(ch) If(invalid.Contains(ch), "_"c, ch)).ToArray()
        Return New String(chars)
    End Function

    Private Function HasOption(args As String(), optionName As String) As Boolean
        Return args.Any(Function(arg) arg.Equals(optionName, StringComparison.OrdinalIgnoreCase))
    End Function

    Private Function GetOptionValue(args As String(), optionName As String) As String
        For i = 0 To args.Length - 2
            If args(i).Equals(optionName, StringComparison.OrdinalIgnoreCase) Then
                Return args(i + 1)
            End If
        Next

        Return ""
    End Function

    Private Function ParseOptionalRomBase(args As String(), startIndex As Integer) As UInteger?
        For i = startIndex To args.Length - 2
            If args(i).Equals("--rom-base", StringComparison.OrdinalIgnoreCase) Then
                Dim value = args(i + 1)
                If value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) Then
                    Return UInteger.Parse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                End If

                Return UInteger.Parse(value, CultureInfo.InvariantCulture)
            End If
        Next

        Return Nothing
    End Function

    Private Function IsHelp(value As String) As Boolean
        Return value = "-h" OrElse value = "--help" OrElse value = "/?"
    End Function

    Private Sub PrintUsage()
        Console.WriteLine("PalmRomExtractor - list and export Palm OS ROM applications")
        Console.WriteLine()
        Console.WriteLine("Usage:")
        Console.WriteLine("  PalmRomExtractor list <rom-file> [--rom-base 0x10C00000]")
        Console.WriteLine("  PalmRomExtractor export <rom-file> <output-dir> [--all] [--name ""Date Book""] [--creator date] [--rom-base 0x10C00000]")
        Console.WriteLine()
        Console.WriteLine("Examples:")
        Console.WriteLine("  dotnet run --project PalmRomExtractor -- list Palm-m100-3.51-en.rom")
        Console.WriteLine("  dotnet run --project PalmRomExtractor -- export Palm-m100-3.51-en.rom ExtractedApps --all")
        Console.WriteLine("  dotnet run --project PalmRomExtractor -- export Palm-m100-3.51-en.rom ExtractedApps --creator cclk --include-related")
        Console.WriteLine("  dotnet run --project PalmRomExtractor -- export Palm-m100-3.51-en.rom ExtractedApps --name ""Memo Pad""")
    End Sub
End Module
