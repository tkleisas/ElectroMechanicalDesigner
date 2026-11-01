namespace Emd.Server;

public static class WebUi
{
    public const string Html = """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <title>EMD project viewer</title>
          <style>
            body { font-family: system-ui, sans-serif; margin: 2rem; max-width: 70rem; }
            input { width: 32rem; padding: .4rem; }
            pre { background: #f4f4f4; padding: 1rem; overflow: auto; }
            button { padding: .45rem .8rem; }
          </style>
        </head>
        <body>
          <h1>EMD project viewer</h1>
          <p>Read-only project, trace, package, and G-code inspection.</p>
          <h2>Project</h2>
          <input id="project" value="examples/project.json">
          <button onclick="loadProject()">Load</button>
          <pre id="projectOut"></pre>
          <h2>Trace CSV</h2>
          <input id="trace" placeholder="traces/move_40mm.csv">
          <button onclick="loadTrace()">Load</button>
          <pre id="traceOut"></pre>
          <h2>Package directory</h2>
          <input id="package" placeholder="out/package">
          <button onclick="loadPackage()">Verify</button>
          <pre id="packageOut"></pre>
          <script>
            async function show(element, url) {
              const out = document.getElementById(element);
              out.textContent = 'loading...';
              try {
                const response = await fetch(url);
                const text = await response.text();
                try { out.textContent = JSON.stringify(JSON.parse(text), null, 2); }
                catch { out.textContent = text; }
              } catch (error) { out.textContent = String(error); }
            }
            function loadProject() {
              show('projectOut', '/api/project?path=' + encodeURIComponent(document.getElementById('project').value));
            }
            function loadTrace() {
              show('traceOut', '/api/trace?path=' + encodeURIComponent(document.getElementById('trace').value));
            }
            function loadPackage() {
              show('packageOut', '/api/package?dir=' + encodeURIComponent(document.getElementById('package').value));
            }
          </script>
        </body>
        </html>
        """;
}
