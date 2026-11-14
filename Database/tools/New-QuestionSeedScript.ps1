<#
.SYNOPSIS
    Builds a re-runnable SQL seed script for dbo.Questions from NEET question workbooks (.xlsx).

.DESCRIPTION
    Each workbook's first sheet must have this header row:
        YEAR | SUBJECT | LESSON | TOPIC | QUESTION | OPTION-A | OPTION-B | OPTION-C | OPTION-D | CORRECT ANSWER | EXPLANATION
    Every row is validated (year, subject, answer letter, required text). Any invalid row stops the script
    and is listed, so nothing half-valid is written. The output uses MERGE on (ExamYear, SourceRow),
    so running it again updates rows instead of duplicating them.

.EXAMPLE
    .\New-QuestionSeedScript.ps1 -Workbook 'C:\data\NEET_2015.xlsx','C:\data\NEET_2016.xlsx' `
        -OutFile ..\005_seed_questions_2015_2017.sql
    sqlcmd -S localhost -E -f 65001 -i ..\005_seed_questions_2015_2017.sql
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string[]]$Workbook,
    [Parameter(Mandatory)][string]$OutFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$expectedHeader = @('YEAR', 'SUBJECT', 'LESSON', 'TOPIC', 'QUESTION', 'OPTION-A', 'OPTION-B', 'OPTION-C', 'OPTION-D', 'CORRECT ANSWER', 'EXPLANATION')
$columns        = @('A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K')
$subjects       = @('Biology', 'Chemistry', 'Physics')

function Read-Worksheet([string]$path) {
    # Returns rows as ordered dictionaries: _row plus one key per column letter.
    $zip = [IO.Compression.ZipFile]::OpenRead($path)
    try {
        function Read-Xml($name) {
            $entry = $zip.GetEntry($name)
            if (-not $entry) { return $null }
            $reader = New-Object IO.StreamReader($entry.Open(), [Text.Encoding]::UTF8)
            try { return [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
        }
        $shared = New-Object System.Collections.Generic.List[string]
        $sst = Read-Xml 'xl/sharedStrings.xml'
        if ($sst) {
            foreach ($si in $sst.DocumentElement.ChildNodes) {
                $shared.Add((($si.SelectNodes('.//*[local-name()="t"]') | ForEach-Object { $_.InnerText }) -join ''))
            }
        }
        $sheet = Read-Xml 'xl/worksheets/sheet1.xml'
        foreach ($row in $sheet.SelectNodes('//*[local-name()="sheetData"]/*[local-name()="row"]')) {
            $cells = [ordered]@{ _row = [int]$row.GetAttribute('r') }
            foreach ($c in $row.SelectNodes('*[local-name()="c"]')) {
                $col = $c.GetAttribute('r') -replace '\d', ''
                $v = $c.SelectSingleNode('*[local-name()="v"]')
                $type = $c.GetAttribute('t')
                $value = $null
                if ($type -eq 's' -and $v) { $value = $shared[[int]$v.InnerText] }
                elseif ($type -eq 'inlineStr') { $value = (($c.SelectNodes('.//*[local-name()="t"]') | ForEach-Object { $_.InnerText }) -join '') }
                elseif ($v) { $value = $v.InnerText }
                $cells[$col] = $value
            }
            $cells
        }
    }
    finally { $zip.Dispose() }
}

function Clean([object]$value) {
    if ($null -eq $value) { return '' }
    return (([string]$value) -replace '\s+', ' ').Trim()
}

function SqlText([string]$value) {
    if ([string]::IsNullOrEmpty($value)) { return 'NULL' }
    return "N'" + $value.Replace("'", "''") + "'"
}

$errors = New-Object System.Collections.Generic.List[string]
$batches = New-Object System.Collections.Generic.List[object]

foreach ($path in $Workbook) {
    $name = Split-Path $path -Leaf
    $rows = @(Read-Worksheet $path)
    if ($rows.Count -lt 2) { $errors.Add("${name}: no data rows"); continue }

    $header = $columns | ForEach-Object { (Clean $rows[0][$_]).ToUpperInvariant() }
    if (($header -join '|') -ne ($expectedHeader -join '|')) {
        $errors.Add("${name}: unexpected header '$($header -join ' | ')'")
        continue
    }

    $values = New-Object System.Collections.Generic.List[string]
    $years = @{}
    foreach ($r in $rows | Select-Object -Skip 1) {
        $cell = @{}
        foreach ($c in $columns) { $cell[$c] = Clean $r[$c] }
        if (($columns | Where-Object { $cell[$_] }).Count -eq 0) { continue }   # blank line

        $where = "${name} row $($r._row)"
        $year = 0
        if (-not [int]::TryParse($cell.A, [ref]$year) -or $year -lt 1988 -or $year -gt 2100) { $errors.Add("${where}: bad YEAR '$($cell.A)'") }
        $subject = $subjects | Where-Object { $_ -eq $cell.B } | Select-Object -First 1
        if (-not $subject) { $errors.Add("${where}: bad SUBJECT '$($cell.B)'") }
        $answer = $cell.J.ToUpperInvariant()
        if ($answer -notin @('A', 'B', 'C', 'D')) { $errors.Add("${where}: bad CORRECT ANSWER '$($cell.J)'") }
        foreach ($required in 'E', 'F', 'G', 'H', 'I') {
            if (-not $cell[$required]) { $errors.Add("${where}: empty column $required") }
        }
        $years[$year] = $true

        $values.Add("    ($year, $(SqlText $subject), $(SqlText $cell.C), $(SqlText $cell.D), $($r._row), $(SqlText $cell.E), " +
            "$(SqlText $cell.F), $(SqlText $cell.G), $(SqlText $cell.H), $(SqlText $cell.I), '$answer', $(SqlText $cell.K))")
    }
    if ($years.Count -gt 1) { $errors.Add("${name}: more than one YEAR in the sheet ($($years.Keys -join ', '))") }
    $batches.Add([pscustomobject]@{ Name = $name; Year = ($years.Keys | Select-Object -First 1); Values = $values })
}

if ($errors.Count) {
    $errors | ForEach-Object { Write-Error $_ -ErrorAction Continue }
    throw "$($errors.Count) problem(s) found; no script was written."
}

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('/* ============================================================================')
[void]$sb.AppendLine('   NEET BEE - question bank seed. Generated by Database\tools\New-QuestionSeedScript.ps1')
[void]$sb.AppendLine("   Source  : $((($batches | ForEach-Object { $_.Name }) -join ', '))")
[void]$sb.AppendLine("   Rows    : $((($batches | ForEach-Object { "$($_.Year)=$($_.Values.Count)" }) -join ', '))")
[void]$sb.AppendLine('   Run after 004. Safe to re-run: rows are matched on (ExamYear, SourceRow) and updated.')
[void]$sb.AppendLine('   sqlcmd -S localhost -E -f 65001 -i 005_seed_questions_2015_2017.sql')
[void]$sb.AppendLine('   ========================================================================== */')
[void]$sb.AppendLine()
[void]$sb.AppendLine('USE NEET_BEE;')
[void]$sb.AppendLine('GO')
[void]$sb.AppendLine('-- The table has filtered indexes, which need these options; sqlcmd turns QUOTED_IDENTIFIER off by default.')
[void]$sb.AppendLine('SET ANSI_NULLS ON;')
[void]$sb.AppendLine('SET QUOTED_IDENTIFIER ON;')
[void]$sb.AppendLine('SET NOCOUNT ON;')
[void]$sb.AppendLine('SET XACT_ABORT ON;')
[void]$sb.AppendLine('GO')

foreach ($b in $batches) {
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("-- $($b.Name): $($b.Values.Count) questions for $($b.Year)")
    [void]$sb.AppendLine('BEGIN TRANSACTION;')
    [void]$sb.AppendLine('MERGE dbo.Questions AS target')
    [void]$sb.AppendLine('USING (VALUES')
    [void]$sb.AppendLine(($b.Values -join ",`r`n"))
    [void]$sb.AppendLine(') AS source (ExamYear, Subject, Lesson, Topic, SourceRow, QuestionText, OptionA, OptionB, OptionC, OptionD, CorrectOption, Explanation)')
    [void]$sb.AppendLine('ON target.ExamYear = source.ExamYear AND target.SourceRow = source.SourceRow')
    [void]$sb.AppendLine('WHEN MATCHED THEN UPDATE SET')
    [void]$sb.AppendLine('    Subject = source.Subject, Lesson = source.Lesson, Topic = source.Topic, QuestionText = source.QuestionText,')
    [void]$sb.AppendLine('    OptionA = source.OptionA, OptionB = source.OptionB, OptionC = source.OptionC, OptionD = source.OptionD,')
    [void]$sb.AppendLine('    CorrectOption = source.CorrectOption, Explanation = source.Explanation, IsActive = 1, UpdatedAt = SYSUTCDATETIME()')
    [void]$sb.AppendLine('WHEN NOT MATCHED BY TARGET THEN INSERT')
    [void]$sb.AppendLine('    (ExamYear, Subject, Lesson, Topic, SourceRow, QuestionText, OptionA, OptionB, OptionC, OptionD, CorrectOption, Explanation)')
    [void]$sb.AppendLine('    VALUES (source.ExamYear, source.Subject, source.Lesson, source.Topic, source.SourceRow, source.QuestionText,')
    [void]$sb.AppendLine('            source.OptionA, source.OptionB, source.OptionC, source.OptionD, source.CorrectOption, source.Explanation);')
    [void]$sb.AppendLine('COMMIT TRANSACTION;')
    [void]$sb.AppendLine('GO')
}

[void]$sb.AppendLine()
[void]$sb.AppendLine('SELECT ExamYear, Subject, COUNT(*) AS Questions FROM dbo.Questions WHERE IsActive = 1 GROUP BY ExamYear, Subject ORDER BY ExamYear, Subject;')
[void]$sb.AppendLine('GO')

# UTF-8 with BOM so sqlcmd and SSMS both read the text correctly.
[IO.File]::WriteAllText($OutFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($true)))
Write-Host "Wrote $OutFile ($((($batches | ForEach-Object { "$($_.Year)=$($_.Values.Count)" }) -join ', ')))"
