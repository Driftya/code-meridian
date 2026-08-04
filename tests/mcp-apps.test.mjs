import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { JSDOM } from 'jsdom';

const appRoot = new URL('../src/McpServer/Apps/', import.meta.url);

async function loadApp(fileName) {
  const messages = [];
  const html = await readFile(new URL(fileName, appRoot), 'utf8');
  const host = { postMessage: message => messages.push(message) };
  const dom = new JSDOM(html, {
    beforeParse(window) {
      Object.defineProperty(window, 'parent', { value: host });
    },
    runScripts: 'dangerously',
    url: 'https://mcp-app.test/'
  });

  function deliver(data) {
    const event = new dom.window.MessageEvent('message', { data });
    Object.defineProperty(event, 'source', { value: host });
    dom.window.dispatchEvent(event);
  }

  return { dom, document: dom.window.document, html, messages, deliver };
}

test('connection viewer renders graph-controlled values as text', async () => {
  const app = await loadApp('connection-viewer.html');
  const hostile = '<img src=x onerror="globalThis.compromised=true">';

  app.deliver({
    jsonrpc: '2.0',
    method: 'ui/notifications/tool-result',
    params: {
      structuredContent: {
        contractVersion: '1.0',
        fromId: hostile,
        toId: 'target',
        maxHops: 10,
        pathFound: true,
        hopCount: 1,
        truncated: false,
        nodes: [
          { order: 0, id: 'a', name: hostile, type: 'Class', namespace: 'Example', filePath: 'src/A.cs', lineNumber: 4 },
          { order: 1, id: 'b', name: 'Target', type: 'Method', filePath: 'src/B.cs' }
        ],
        edges: [{ order: 0, sourceId: 'a', targetId: 'b', relationship: hostile }],
        frontendSignals: [hostile]
      }
    }
  });

  assert.match(app.document.getElementById('content').textContent, /<img src=x/);
  assert.equal(app.document.querySelectorAll('#content img').length, 0);
  assert.equal(app.document.querySelectorAll('#content script').length, 0);
  assert.equal(app.dom.window.compromised, undefined);
  assert.equal(app.document.querySelectorAll('.path-node').length, 2);
  assert.equal(app.document.querySelector('.edge').textContent, hostile);
  app.dom.window.close();
});

test('connection viewer exposes a keyboard and screen-reader friendly text path', async () => {
  const app = await loadApp('connection-viewer.html');
  const refresh = app.document.getElementById('refresh');
  const status = app.document.getElementById('status');

  assert.equal(app.document.documentElement.lang, 'en');
  assert.ok(app.document.querySelector('main'));
  assert.equal(refresh.tagName, 'BUTTON');
  assert.equal(refresh.type, 'button');
  assert.equal(status.getAttribute('role'), 'status');
  assert.equal(status.getAttribute('aria-live'), 'polite');
  assert.ok(app.document.querySelector('noscript'));

  app.deliver({
    jsonrpc: '2.0',
    method: 'ui/notifications/tool-result',
    params: {
      structuredContent: {
        contractVersion: '1.0', fromId: 'a', toId: 'b', maxHops: 10,
        pathFound: true, hopCount: 1, truncated: false,
        nodes: [
          { order: 0, id: 'a', name: 'A', type: 'Class' },
          { order: 1, id: 'b', name: 'B', type: 'Class' }
        ],
        edges: [{ order: 0, sourceId: 'a', targetId: 'b', relationship: 'Calls' }],
        frontendSignals: []
      }
    }
  });

  assert.equal(app.document.querySelector('ol.path').getAttribute('aria-label'), 'Ordered connection path');
  assert.equal(app.document.querySelector('.edge').getAttribute('aria-label'), 'then Calls');
  assert.equal(app.document.getElementById('content').getAttribute('aria-busy'), 'false');
  assert.equal(refresh.disabled, false);
  app.dom.window.close();
});

test('MCP App documents avoid executable HTML sinks and external assets', async () => {
  for (const fileName of ['client-extension-contract.html', 'connection-viewer.html']) {
    const app = await loadApp(fileName);
    assert.doesNotMatch(app.html, /\.innerHTML\s*=/i, fileName);
    assert.doesNotMatch(app.html, /insertAdjacentHTML|document\.write|\beval\s*\(|new\s+Function\s*\(/i, fileName);
    assert.equal(app.document.querySelectorAll('script[src], link[rel="stylesheet"], iframe, object, embed').length, 0, fileName);
    assert.doesNotMatch(app.html, /CodeMeridian_Auth_ApiKey|X-CodeMeridian-ApiKey|Authorization:\s*Bearer/i, fileName);
    app.dom.window.close();
  }
});
