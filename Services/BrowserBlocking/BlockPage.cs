namespace Regex_Site_Blocker_App.Services.BrowserBlocking
{
    public static class BlockPage
    {
        public const string Html = """
            <!doctype html>
            <html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; base-uri 'none'; form-action 'none'">
            <title>사이트 접근 차단</title><style>
            body{margin:0;background:#f3f5fa;color:#182234;font:17px/1.65 system-ui,sans-serif;display:grid;min-height:100vh;place-items:center}
            main{max-width:560px;margin:24px;padding:40px;background:white;border-radius:20px;box-shadow:0 12px 40px #18223412}
            .badge{color:#6548bd;font-weight:700;font-size:14px}h1{font-size:30px;line-height:1.3}#host{overflow-wrap:anywhere;font-weight:600}
            p{color:#526078}footer{margin-top:30px;font-size:13px;color:#758093}
            </style></head><body><main><div class="badge">Regex Site Blocker</div><h1>사이트 접근이 차단되었습니다</h1>
            <p id="host"></p><p>활성화된 차단 규칙에 해당하는 사이트입니다.<br>규칙을 변경하려면 트레이에서 앱 설정을 여세요.</p>
            <footer>브라우저에 표시된 페이지를 검사하여 이동을 제한했습니다.</footer></main>
            <script>try{document.getElementById('host').textContent=decodeURIComponent(location.hash.slice(1));}catch{}</script>
            </body></html>
            """;

        public static string Create(string directory)
        {
            Directory.CreateDirectory(directory);
            string path = Path.GetFullPath(Path.Combine(directory, "blocked.html"));
            File.WriteAllText(path, Html, new System.Text.UTF8Encoding(false));
            return new Uri(path).AbsoluteUri;
        }
        public static string Destination(string pageUrl, string host) => pageUrl + "#" + Uri.EscapeDataString(host);
    }
}
