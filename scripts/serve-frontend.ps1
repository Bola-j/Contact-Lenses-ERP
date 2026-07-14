param(
    [int]$Port = 3000,
    [string]$Root = (Join-Path $PSScriptRoot "..\frontend")
)

$resolvedRoot = (Resolve-Path $Root).Path
$listener = [System.Net.HttpListener]::new()
$prefix = "http://127.0.0.1:$Port/"
$listener.Prefixes.Add($prefix)

function Get-ContentType {
    param([string]$Path)

    switch ([System.IO.Path]::GetExtension($Path).ToLowerInvariant()) {
        ".html" { "text/html; charset=utf-8"; break }
        ".css" { "text/css; charset=utf-8"; break }
        ".js" { "text/javascript; charset=utf-8"; break }
        ".json" { "application/json; charset=utf-8"; break }
        ".svg" { "image/svg+xml"; break }
        ".png" { "image/png"; break }
        ".jpg" { "image/jpeg"; break }
        ".jpeg" { "image/jpeg"; break }
        default { "application/octet-stream" }
    }
}

$listener.Start()
Write-Host "Serving $resolvedRoot at $prefix"
Write-Host "Press Ctrl+C to stop."

try {
    while ($listener.IsListening) {
        $context = $listener.GetContext()
        try {
            $requestPath = [Uri]::UnescapeDataString($context.Request.Url.AbsolutePath.TrimStart("/"))
            if ([string]::IsNullOrWhiteSpace($requestPath)) {
                $requestPath = "index.html"
            }

            $fullPath = [System.IO.Path]::GetFullPath((Join-Path $resolvedRoot $requestPath))
            if (-not $fullPath.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                $context.Response.StatusCode = 403
                $context.Response.Close()
                continue
            }

            if (-not [System.IO.File]::Exists($fullPath)) {
                $fullPath = Join-Path $resolvedRoot "index.html"
            }

            $bytes = [System.IO.File]::ReadAllBytes($fullPath)
            $context.Response.ContentType = Get-ContentType $fullPath
            $context.Response.ContentLength64 = $bytes.Length
            $context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
            $context.Response.Close()
        }
        catch {
            if ($context.Response.OutputStream.CanWrite) {
                $context.Response.StatusCode = 500
                $context.Response.Close()
            }
        }
    }
}
finally {
    $listener.Stop()
    $listener.Close()
}
