# 発行済みexeのバージョン情報の言語を「ニュートラル」→「日本語(0x0411)」に書き換える。
# .NETが生成するVERSIONINFOは言語が常にニュートラルになるため、
# 同じ長さのまま翻訳指定だけを差し替える(単一ファイルexeのバンドルを壊さない)。
param([Parameter(Mandatory)][string]$ExePath)

$bytes = [System.IO.File]::ReadAllBytes($ExePath)
$enc = [System.Text.Encoding]::Unicode

# PEヘッダから .rsrc セクションの範囲を求める(バンドル部分のDLLを誤って書き換えないため)
$peOff = [BitConverter]::ToInt32($bytes, 0x3C)
$numSec = [BitConverter]::ToUInt16($bytes, $peOff + 6)
$optSize = [BitConverter]::ToUInt16($bytes, $peOff + 20)
$secTable = $peOff + 24 + $optSize
$rsrcStart = -1; $rsrcEnd = -1
for ($i = 0; $i -lt $numSec; $i++) {
    $off = $secTable + $i * 40
    $name = [System.Text.Encoding]::ASCII.GetString($bytes, $off, 8).TrimEnd([char]0)
    if ($name -eq '.rsrc') {
        $rsrcStart = [BitConverter]::ToUInt32($bytes, $off + 20)
        $rsrcEnd = $rsrcStart + [BitConverter]::ToUInt32($bytes, $off + 16)
        break
    }
}
if ($rsrcStart -lt 0) { throw '.rsrc セクションが見つかりません' }

function Find-Pattern([byte[]]$data, [byte[]]$pattern, [int]$from, [int]$to) {
    for ($i = $from; $i -le $to - $pattern.Length; $i++) {
        $ok = $true
        for ($j = 0; $j -lt $pattern.Length; $j++) {
            if ($data[$i + $j] -ne $pattern[$j]) { $ok = $false; break }
        }
        if ($ok) { return $i }
    }
    return -1
}

# 1) StringFileInfo のブロック名 "000004b0" (UTF-16LE、ニュートラル言語) → "041104b0"
#    ※ ランタイムDLL由来のブロックは "040904B0" なので一致しない
$oldKey = $enc.GetBytes('000004b0')
$keyPos = Find-Pattern $bytes $oldKey $rsrcStart $rsrcEnd
if ($keyPos -lt 0) { throw 'StringFileInfo のニュートラル言語キーが見つかりません' }
[Array]::Copy($enc.GetBytes('041104b0'), 0, $bytes, $keyPos, $oldKey.Length)

# 2) 同じブロック内 VarFileInfo の "Translation" 値: 言語0x0000+CP0x04B0 → 0x0411+0x04B0
$transKey = $enc.GetBytes('Translation')
$searchFrom = [Math]::Max($rsrcStart, $keyPos - 1024)
$transPos = Find-Pattern $bytes $transKey $searchFrom $rsrcEnd
if ($transPos -lt 0) { throw 'Translation キーが見つかりません' }
$patched = $false
for ($i = $transPos + $transKey.Length; $i -lt $transPos + $transKey.Length + 16; $i++) {
    if ($bytes[$i] -eq 0 -and $bytes[$i+1] -eq 0 -and $bytes[$i+2] -eq 0xB0 -and $bytes[$i+3] -eq 0x04) {
        $bytes[$i] = 0x11; $bytes[$i+1] = 0x04
        $patched = $true
        break
    }
}
if (-not $patched) { throw 'Translation の値が見つかりません' }

[System.IO.File]::WriteAllBytes($ExePath, $bytes)
$lang = (Get-Item $ExePath).VersionInfo.Language
Write-Host "OK: 言語 = $lang"
if ($lang -notmatch '日本語|Japanese') { throw '言語の書き換えに失敗しました' }
