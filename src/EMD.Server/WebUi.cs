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
            :root { color-scheme: light dark; }
            body { font-family: system-ui, sans-serif; margin: 2rem; max-width: 90rem; }
            input { width: 34rem; padding: .4rem; }
            button { padding: .45rem .8rem; margin-left: .3rem; }
            section { margin-top: 1.5rem; }
            pre { background: rgba(127,127,127,.12); padding: 1rem; overflow: auto; }
            table { border-collapse: collapse; }
            th, td { border: 1px solid rgba(127,127,127,.35); padding: .25rem .5rem; text-align: left; }
            svg { background: rgba(127,127,127,.06); border-radius: .4rem; max-width: 100%; }
            .muted { opacity: .7; }
            .error { color: #c0392b; }
          </style>
        </head>
        <body>
          <h1>EMD project viewer</h1>
          <p class="muted">Read-only project graph, isometric scene, validation, package, and trace inspection.</p>
          <label>Project <input id="project" value="examples/project.json"></label>
          <button onclick="loadProject()">Load project</button>
          <section><h2>Project summary</h2><pre id="summary">Loading...</pre></section>
          <section><h2>Project graph</h2><svg id="graph" width="900" height="430"></svg></section>
          <section><h2>Isometric scene</h2><svg id="scene" width="900" height="500"></svg></section>
          <section><h2>Validation</h2><pre id="validation"></pre></section>
          <section><h2>Package</h2>
            <input id="package" placeholder="out/package"><button onclick="loadPackage()">Verify</button>
            <pre id="packageOut"></pre>
          </section>
          <section><h2>Trace chart</h2>
            <input id="trace" placeholder="traces/move_40mm.csv"><button onclick="loadTrace()">Chart</button>
            <svg id="chart" width="900" height="300"></svg>
            <pre id="traceOut"></pre>
          </section>
          <script>
            const svgNS = 'http://www.w3.org/2000/svg';
            const projectPath = () => document.getElementById('project').value;
            const enc = encodeURIComponent;
            function text(value) { return value === null || value === undefined ? '' : String(value); }
            function element(name, attrs = {}, parent = null) {
              const node = document.createElementNS(svgNS, name);
              for (const [key, value] of Object.entries(attrs)) node.setAttribute(key, value);
              if (parent) parent.appendChild(node);
              return node;
            }
            async function getJson(url) {
              const response = await fetch(url);
              const body = await response.text();
              try { return JSON.parse(body); }
              catch { return { error: body || response.statusText }; }
            }
            function clear(node) { while (node.firstChild) node.removeChild(node.firstChild); }

            async function loadProject() {
              const path = projectPath();
              const [summary, graph, scene, validation] = await Promise.all([
                getJson('/api/project?path=' + enc(path)),
                getJson('/api/graph?path=' + enc(path)),
                getJson('/api/scene?path=' + enc(path)),
                getJson('/api/validate?path=' + enc(path))
              ]);
              document.getElementById('summary').textContent = JSON.stringify(summary, null, 2);
              drawGraph(graph, document.getElementById('graph'));
              drawScene(scene, document.getElementById('scene'));
              document.getElementById('validation').textContent =
                Array.isArray(validation) && validation.length
                  ? validation.map(d => `${d.severity} ${d.code}: ${d.message}`).join('\n')
                  : 'No diagnostics.';
            }

            function drawGraph(graph, svg) {
              clear(svg);
              const nodes = graph.nodes || [];
              const edges = graph.edges || [];
              const cx = 450, cy = 215, radius = 155;
              const positions = new Map();
              nodes.forEach((node, index) => {
                const angle = (2 * Math.PI * index) / Math.max(1, nodes.length) - Math.PI / 2;
                positions.set(node.id, {
                  x: cx + radius * Math.cos(angle),
                  y: cy + radius * Math.sin(angle),
                  node
                });
              });
              for (const edge of edges) {
                const a = positions.get(edge.from), b = positions.get(edge.to);
                if (!a || !b) continue;
                element('line', { x1: a.x, y1: a.y, x2: b.x, y2: b.y,
                  stroke: 'currentColor', 'stroke-opacity': .25 }, svg);
              }
              for (const item of positions.values()) {
                element('circle', { cx: item.x, cy: item.y, r: 16,
                  fill: 'rgba(52,152,219,.16)', stroke: 'currentColor' }, svg);
                const label = element('text', { x: item.x, y: item.y + 32,
                  'text-anchor': 'middle', 'font-size': 11, fill: 'currentColor' }, svg);
                label.textContent = item.node.key || item.node.name;
              }
            }

            function drawScene(scene, svg) {
              clear(svg);
              const parts = scene.parts || [];
              if (!parts.length) { svg.textContent = 'No parts.'; return; }
              const project = (x, y, z) => ({ x: (x - y) * 0.866, y: (x + y) * 0.5 - z });
              let minX = 1e9, minY = 1e9, maxX = -1e9, maxY = -1e9;
              const boxes = parts.map(part => {
                const pos = part.pos || [0, 0, 0];
                const size = part.size || [.02, .02, .02];
                const x = pos[0] * 1000, y = pos[1] * 1000, z = pos[2] * 1000;
                const sx = size[0] * 1000, sy = size[1] * 1000, sz = size[2] * 1000;
                const points = [];
                for (const [dx, dy, dz] of [[0,0,0],[sx,0,0],[sx,sy,0],[0,sy,0],
                    [0,0,sz],[sx,0,sz],[sx,sy,sz],[0,sy,sz]]) {
                  const p = project(x + dx, y + dy, z + dz);
                  points.push(p);
                  minX = Math.min(minX, p.x); maxX = Math.max(maxX, p.x);
                  minY = Math.min(minY, p.y); maxY = Math.max(maxY, p.y);
                }
                return { part, points };
              });
              const scale = Math.min(820 / Math.max(1, maxX - minX), 420 / Math.max(1, maxY - minY));
              const tx = p => 40 + (p.x - minX) * scale;
              const ty = p => 40 + (p.y - minY) * scale;
              for (const box of boxes) {
                const faces = [[0,1,2,3],[4,5,6,7],[0,1,5,4],[1,2,6,5],[2,3,7,6],[3,0,4,7]];
                for (const [index, face] of faces.entries()) {
                  const polygon = element('polygon', {
                    points: face.map(i => `${tx(box.points[i])},${ty(box.points[i])}`).join(' '),
                    fill: `rgba(52,152,219,${0.08 + index * 0.035})`,
                    stroke: 'currentColor', 'stroke-opacity': .35
                  }, svg);
                  polygon.appendChild(document.createTextNode(''));
                }
                const top = box.points[4];
                const label = element('text', { x: tx(top), y: ty(top) - 4,
                  'font-size': 10, fill: 'currentColor', 'text-anchor': 'middle' }, svg);
                label.textContent = box.part.key || box.part.name;
              }
            }

            async function loadPackage() {
              const dir = document.getElementById('package').value;
              const data = await getJson('/api/package?dir=' + enc(dir));
              document.getElementById('packageOut').textContent = JSON.stringify(data, null, 2);
            }

            async function loadTrace() {
              const path = document.getElementById('trace').value;
              const data = await getJson('/api/trace-chart?path=' + enc(path));
              const svg = document.getElementById('chart');
              clear(svg);
              document.getElementById('traceOut').textContent = data.error || '';
              if (!data.rows || !data.rows.length) { svg.textContent = 'No trace rows.'; return; }
              const signals = data.signals || [];
              const signal = signals.includes('x_carriage_m') ? 'x_carriage_m' : signals[0];
              if (!signal) { svg.textContent = 'No signals.'; return; }
              const values = data.rows.map(row => Number(row[signal])).filter(Number.isFinite);
              if (!values.length) { svg.textContent = `Signal '${signal}' has no numeric values.`; return; }
              const min = Math.min(...values), max = Math.max(...values);
              const span = max - min || 1;
              const points = values.map((value, index) => `${40 + (index / Math.max(1, values.length - 1)) * 820},${260 - ((value - min) / span) * 210}`).join(' ');
              element('polyline', { points, fill: 'none', stroke: '#3498db', 'stroke-width': 2 }, svg);
              element('text', { x: 40, y: 20, fill: 'currentColor', 'font-size': 12 }, svg).textContent = signal;
              element('text', { x: 40, y: 290, fill: 'currentColor', 'font-size': 11 }, svg).textContent = `min ${min.toFixed(3)} max ${max.toFixed(3)}`;
              document.getElementById('traceOut').textContent = `${values.length} samples`;
            }
          </script>
        </body>
        </html>
        """;
}
