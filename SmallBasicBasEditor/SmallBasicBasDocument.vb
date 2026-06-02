Imports System
Imports System.Collections.Generic
Imports System.IO
Imports System.Text

Namespace SmallBasicBasEditor
    Friend NotInheritable Class SmallBasicBasSection
        Public Sub New(name As String, source As String)
            Me.Name = name
            Me.Source = source
        End Sub

        Public Property Name As String
        Public Property Source As String

        Public Overrides Function ToString() As String
            Return Name
        End Function
    End Class

    Friend NotInheritable Class SmallBasicBasDocument
        Private Const PalmEpochYear As Integer = 1904
        Private Const HeaderLength As Integer = 78
        Private Const RecordEntryLength As Integer = 8
        Private Const MaxTextRecordBytes As Integer = 4096

        Private Shared ReadOnly Ascii As Encoding = Encoding.ASCII

        Private Sub New()
        End Sub

        Public Shared Function IsSmallBasicDatabase(bytes As Byte()) As Boolean
            If bytes Is Nothing OrElse bytes.Length < HeaderLength Then Return False
            Return ReadAscii(bytes, 60, 4) = "TEXT" AndAlso ReadAscii(bytes, 64, 4) = "SmBa"
        End Function

        Public Shared Function ReadSource(path As String) As String
            Dim sections = ReadSections(path)
            Dim sources As New List(Of String)()
            For Each section In sections
                sources.Add(section.Source)
            Next

            Return String.Join(Environment.NewLine & Environment.NewLine, sources)
        End Function

        Public Shared Function ReadSections(path As String) As List(Of SmallBasicBasSection)
            Dim bytes = File.ReadAllBytes(path)
            If Not IsSmallBasicDatabase(bytes) Then
                Return New List(Of SmallBasicBasSection) From {
                    New SmallBasicBasSection("Plain text", DecodeTextFile(bytes))
                }
            End If

            Dim records = ReadRecords(bytes)
            Dim sections As New List(Of SmallBasicBasSection)()
            For recordIndex = 0 To records.Count - 1
                Dim record = records(recordIndex)
                Dim recordName = ReadRecordSectionName(record)
                For Each segment In ExtractPrintableSegments(record)
                    Dim trimmedSegment = TrimToSourceBoundary(segment)
                    If LooksLikeSource(trimmedSegment) Then
                        Dim source = NormalizeNewLines(trimmedSegment.TrimEnd(ChrW(0), " "c, ControlChars.Tab))
                        Dim sectionName = If(String.IsNullOrWhiteSpace(recordName), GuessSectionName(source, sections.Count + 1, recordIndex + 1), recordName)
                        sections.Add(New SmallBasicBasSection(sectionName, source))
                    End If
                Next
            Next

            If sections.Count = 0 Then
                For recordIndex = 0 To records.Count - 1
                    Dim record = records(recordIndex)
                    Dim direct = DecodeMostlyPrintable(record)
                    If Not String.IsNullOrWhiteSpace(direct) Then
                        Dim source = NormalizeNewLines(direct.Trim())
                        sections.Add(New SmallBasicBasSection($"Record {recordIndex + 1}", source))
                    End If
                Next
            End If

            If sections.Count = 0 Then
                sections.Add(New SmallBasicBasSection("Section 1", String.Empty))
            End If

            Return sections
        End Function

        Public Shared Sub WriteSource(path As String, source As String)
            WriteSections(path, New List(Of SmallBasicBasSection) From {
                New SmallBasicBasSection("Section 1", source)
            })
        End Sub

        Public Shared Sub WriteSections(path As String, sections As IReadOnlyList(Of SmallBasicBasSection))
            Dim name = IO.Path.GetFileName(path)
            If String.IsNullOrWhiteSpace(name) Then name = "SmallBASIC"

            Dim chunks As New List(Of Byte())()
            For Each section In sections
                AddSourceChunks(chunks, section.Source)
            Next

            If chunks.Count = 0 Then chunks.Add(New Byte() {0})

            Using output As New MemoryStream()
                WriteHeader(output, name, chunks.Count)

                Dim dataOffset = HeaderLength + chunks.Count * RecordEntryLength
                For i = 0 To chunks.Count - 1
                    WriteUInt32BE(output, CUInt(dataOffset))
                    output.WriteByte(&H40)
                    output.WriteByte(CByte((i >> 16) And &HFF))
                    output.WriteByte(CByte((i >> 8) And &HFF))
                    output.WriteByte(CByte(i And &HFF))
                    dataOffset += chunks(i).Length
                Next

                For Each chunk In chunks
                    output.Write(chunk, 0, chunk.Length)
                Next

                File.WriteAllBytes(path, output.ToArray())
            End Using
        End Sub

        Private Shared Sub AddSourceChunks(chunks As List(Of Byte()), source As String)
            Dim sourceBytes = Ascii.GetBytes(NormalizeNewLines(source).Replace(Environment.NewLine, vbLf))
            Dim offset = 0

            If sourceBytes.Length = 0 Then
                chunks.Add(New Byte() {0})
                Return
            End If

            While offset < sourceBytes.Length
                Dim take = Math.Min(MaxTextRecordBytes - 1, sourceBytes.Length - offset)
                Dim chunk(take) As Byte
                Buffer.BlockCopy(sourceBytes, offset, chunk, 0, take)
                chunk(take) = 0
                chunks.Add(chunk)
                offset += take
            End While
        End Sub

        Private Shared Function ReadRecordSectionName(record As Byte()) As String
            If record.Length < 8 OrElse record(0) <> &H53 Then Return String.Empty

            Dim nameStart = 6
            Dim nameEnd = nameStart
            While nameEnd < record.Length AndAlso record(nameEnd) >= 32 AndAlso record(nameEnd) <= 126
                nameEnd += 1
            End While

            If nameEnd = nameStart Then Return String.Empty
            Return Ascii.GetString(record, nameStart, nameEnd - nameStart).Trim()
        End Function

        Private Shared Function ReadRecords(bytes As Byte()) As List(Of Byte())
            Dim count = ReadUInt16BE(bytes, 76)
            Dim starts As New List(Of Integer)()
            For i = 0 To count - 1
                Dim entry = HeaderLength + i * RecordEntryLength
                If entry + 4 > bytes.Length Then Exit For

                Dim start = CInt(ReadUInt32BE(bytes, entry))
                If start >= HeaderLength AndAlso start < bytes.Length Then
                    starts.Add(start)
                End If
            Next

            starts.Sort()

            Dim records As New List(Of Byte())()
            For i = 0 To starts.Count - 1
                Dim start = starts(i)
                Dim finish = If(i + 1 < starts.Count, starts(i + 1), bytes.Length)
                If finish > start Then
                    Dim record(finish - start - 1) As Byte
                    Buffer.BlockCopy(bytes, start, record, 0, record.Length)
                    records.Add(record)
                End If
            Next

            Return records
        End Function

        Private Shared Function ExtractPrintableSegments(record As Byte()) As IEnumerable(Of String)
            Dim segments As New List(Of String)()
            Dim builder As New StringBuilder()

            For Each value In record
                If value = 0 Then
                    AddSegment(segments, builder)
                ElseIf value = 9 OrElse value = 10 OrElse value = 13 OrElse (value >= 32 AndAlso value <= 126) Then
                    builder.Append(ChrW(value))
                Else
                    AddSegment(segments, builder)
                End If
            Next

            AddSegment(segments, builder)
            Return segments
        End Function

        Private Shared Sub AddSegment(segments As List(Of String), builder As StringBuilder)
            If builder.Length >= 4 Then segments.Add(builder.ToString())
            builder.Clear()
        End Sub

        Private Shared Function LooksLikeSource(text As String) As Boolean
            Dim sample = text.Trim()
            If sample.Length < 4 Then Return False
            If sample.Contains(vbLf) OrElse sample.Contains(vbCr) Then Return True

            Dim lower = sample.ToLowerInvariant()
            Return lower.StartsWith("'") OrElse
                lower.StartsWith("rem ") OrElse
                lower.StartsWith("cls") OrElse
                lower.StartsWith("print") OrElse
                lower.StartsWith("input") OrElse
                lower.StartsWith("let ") OrElse
                lower.StartsWith("label ") OrElse
                lower.StartsWith("sub ") OrElse
                lower.StartsWith("func ")
        End Function

        Private Shared Function TrimToSourceBoundary(text As String) As String
            Dim value = text.TrimStart(ChrW(0), " "c, ControlChars.Tab)
            If value.Length = 0 Then Return value

            Dim best = FindFirstSourceBoundary(value)
            If best > 0 Then
                Return value.Substring(best).TrimStart()
            End If

            Return value
        End Function

        Private Shared Function FindFirstSourceBoundary(value As String) As Integer
            Dim normalized = value.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf)
            Dim offset = 0

            For Each line In normalized.Split(ControlChars.Lf)
                Dim leading = line.Length - line.TrimStart(" "c, ControlChars.Tab).Length
                Dim lineStart = offset + leading
                Dim trimmed = line.Substring(leading)

                If StartsLikeSourceLine(trimmed) Then Return lineStart

                Dim commentStart = trimmed.IndexOf("'"c)
                If commentStart >= 0 Then Return lineStart + commentStart

                offset += line.Length + 1
            Next

            Return -1
        End Function

        Private Shared Function StartsLikeSourceLine(line As String) As Boolean
            If line.Length = 0 Then Return False
            If line.StartsWith("'") Then Return True

            Dim lower = line.ToLowerInvariant()
            Return lower.StartsWith("rem ") OrElse
                lower = "cls" OrElse
                lower.StartsWith("cls ") OrElse
                lower.StartsWith("print") OrElse
                lower.StartsWith("input") OrElse
                lower.StartsWith("let ") OrElse
                lower.StartsWith("label ") OrElse
                lower.StartsWith("goto ") OrElse
                lower.StartsWith("gosub ") OrElse
                lower.StartsWith("return") OrElse
                lower.StartsWith("if ") OrElse
                lower.StartsWith("for ") OrElse
                lower.StartsWith("while ") OrElse
                lower.StartsWith("sub ") OrElse
                lower.StartsWith("func ")
        End Function

        Private Shared Function GuessSectionName(source As String, sectionNumber As Integer, recordNumber As Integer) As String
            For Each rawLine In source.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Split(ControlChars.Lf)
                Dim line = rawLine.Trim()
                If line.Length = 0 Then Continue For

                If line.StartsWith("'") Then
                    Dim title = line.TrimStart("'"c).Trim()
                    If title.Length > 0 Then Return title
                End If

                Dim lower = line.ToLowerInvariant()
                If lower.StartsWith("label ") Then Return line
                If lower.StartsWith("sub ") Then Return line
                If lower.StartsWith("func ") Then Return line

                Exit For
            Next

            Return $"Section {sectionNumber} (record {recordNumber})"
        End Function

        Private Shared Function DecodeMostlyPrintable(record As Byte()) As String
            Dim builder As New StringBuilder()
            For Each value In record
                If value = 9 OrElse value = 10 OrElse value = 13 OrElse (value >= 32 AndAlso value <= 126) Then
                    builder.Append(ChrW(value))
                ElseIf builder.Length > 0 AndAlso builder(builder.Length - 1) <> " "c Then
                    builder.Append(" "c)
                End If
            Next

            Return builder.ToString()
        End Function

        Private Shared Function DecodeTextFile(bytes As Byte()) As String
            If bytes.Length >= 3 AndAlso bytes(0) = &HEF AndAlso bytes(1) = &HBB AndAlso bytes(2) = &HBF Then
                Return New UTF8Encoding(False, True).GetString(bytes, 3, bytes.Length - 3)
            End If

            Return Encoding.Default.GetString(bytes)
        End Function

        Private Shared Sub WriteHeader(output As Stream, name As String, recordCount As Integer)
            Dim nameBytes(31) As Byte
            Dim encodedName = Ascii.GetBytes(name)
            Buffer.BlockCopy(encodedName, 0, nameBytes, 0, Math.Min(31, encodedName.Length))
            output.Write(nameBytes, 0, nameBytes.Length)

            WriteUInt16BE(output, 0)
            WriteUInt16BE(output, 0)

            Dim now = PalmTimestamp(DateTimeOffset.UtcNow)
            WriteUInt32BE(output, now)
            WriteUInt32BE(output, now)
            WriteUInt32BE(output, 0)
            WriteUInt32BE(output, now)
            WriteUInt32BE(output, 0)
            WriteUInt32BE(output, 0)
            WriteUInt32BE(output, 0)
            WriteAscii(output, "TEXT")
            WriteAscii(output, "SmBa")
            WriteUInt32BE(output, CUInt(recordCount))
            WriteUInt32BE(output, 0)
            WriteUInt16BE(output, CUShort(recordCount))
        End Sub

        Private Shared Function NormalizeNewLines(value As String) As String
            Return value.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Replace(vbLf, Environment.NewLine)
        End Function

        Private Shared Function PalmTimestamp(value As DateTimeOffset) As UInteger
            Dim epoch As New DateTimeOffset(PalmEpochYear, 1, 1, 0, 0, 0, TimeSpan.Zero)
            Return CUInt(Math.Max(0, CLng((value - epoch).TotalSeconds)))
        End Function

        Private Shared Function ReadAscii(bytes As Byte(), offset As Integer, length As Integer) As String
            Return Ascii.GetString(bytes, offset, length)
        End Function

        Private Shared Function ReadUInt16BE(bytes As Byte(), offset As Integer) As UShort
            Return CUShort((CUInt(bytes(offset)) << 8) Or bytes(offset + 1))
        End Function

        Private Shared Function ReadUInt32BE(bytes As Byte(), offset As Integer) As UInteger
            Return (CUInt(bytes(offset)) << 24) Or (CUInt(bytes(offset + 1)) << 16) Or (CUInt(bytes(offset + 2)) << 8) Or bytes(offset + 3)
        End Function

        Private Shared Sub WriteAscii(output As Stream, value As String)
            Dim bytes = Ascii.GetBytes(value)
            output.Write(bytes, 0, bytes.Length)
        End Sub

        Private Shared Sub WriteUInt16BE(output As Stream, value As UShort)
            output.WriteByte(CByte((value >> 8) And &HFF))
            output.WriteByte(CByte(value And &HFF))
        End Sub

        Private Shared Sub WriteUInt32BE(output As Stream, value As UInteger)
            output.WriteByte(CByte((value >> 24) And &HFFUI))
            output.WriteByte(CByte((value >> 16) And &HFFUI))
            output.WriteByte(CByte((value >> 8) And &HFFUI))
            output.WriteByte(CByte(value And &HFFUI))
        End Sub
    End Class
End Namespace
