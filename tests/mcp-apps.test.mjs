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
  for (const fileName of ['client-extension-contract.html', 'connection-viewer.html', 'change-context-challenge.html']) {
    const app = await loadApp(fileName);
    assert.doesNotMatch(app.html, /\.innerHTML\s*=/i, fileName);
    assert.doesNotMatch(app.html, /insertAdjacentHTML|document\.write|\beval\s*\(|new\s+Function\s*\(/i, fileName);
    assert.equal(app.document.querySelectorAll('script[src], link[rel="stylesheet"], iframe, object, embed').length, 0, fileName);
    assert.doesNotMatch(app.html, /CodeMeridian_Auth_ApiKey|X-CodeMeridian-ApiKey|Authorization:\s*Bearer/i, fileName);
    app.dom.window.close();
  }
});

test('change-context challenge halts on a wrong answer, allows retry, and unlocks notes after success', async () => {
  const app = await loadApp('change-context-challenge.html');
  const challenge = {
    contractVersion: '1.0',
    challengeId: 'challenge-1',
    nodeId: 'Example.Service.Save()',
    question: 'Which two implementations preserve the boundary?',
    requiredSelectionCount: 2,
    choices: [
      { id: 'A', code: 'return Validate(input);' },
      { id: 'B', code: 'return input;' },
      { id: 'C', code: 'return validator.Check(input);' },
      { id: 'D', code: 'return null;' }
    ],
    attempt: 0,
    state: 'awaiting-answer',
    expiresAt: '2026-08-13T12:00:00Z',
    trustNotice: 'Verify against current source and tests.'
  };

  app.deliver({
    jsonrpc: '2.0',
    method: 'ui/notifications/tool-result',
    params: { structuredContent: challenge }
  });

  const inputs = app.document.querySelectorAll('input[name="answer"]');
  assert.equal(inputs.length, 4);
  assert.equal(inputs[0].type, 'checkbox');
  inputs[1].click();
  inputs[2].click();
  app.document.getElementById('challenge-form').dispatchEvent(
    new app.dom.window.Event('submit', { bubbles: true, cancelable: true }));
  await new Promise(resolve => setImmediate(resolve));

  const wrongCall = app.messages.find(message =>
    message.method === 'tools/call' && message.params?.name === 'answer_change_context_challenge');
  assert.deepEqual(Array.from(wrongCall.params.arguments.selectedChoiceIds), ['B', 'C']);
  app.deliver({
    jsonrpc: '2.0',
    id: wrongCall.id,
    result: {
      structuredContent: {
        contractVersion: '1.0', challengeId: 'challenge-1', isCorrect: false,
        halted: true, canRetry: true, attempt: 1, state: 'halted-for-retry',
        selectedChoiceIds: ['B', 'C'],
        feedback: [{ choiceId: 'B', message: 'This bypasses validation.' }]
      }
    }
  });
  await new Promise(resolve => setImmediate(resolve));

  assert.match(app.document.getElementById('status').textContent, /Attempt halted/);
  assert.match(app.document.querySelector('.feedback').textContent, /bypasses validation/);
  assert.equal(app.document.getElementById('note-section').hidden, true);
  assert.equal(app.document.querySelector('[data-choice-id="B"]').classList.contains('wrong'), true);

  inputs[1].click();
  inputs[0].click();
  app.document.getElementById('challenge-form').dispatchEvent(
    new app.dom.window.Event('submit', { bubbles: true, cancelable: true }));
  await new Promise(resolve => setImmediate(resolve));
  const answerCalls = app.messages.filter(message =>
    message.method === 'tools/call' && message.params?.name === 'answer_change_context_challenge');
  const correctCall = answerCalls.at(-1);
  assert.deepEqual(Array.from(correctCall.params.arguments.selectedChoiceIds), ['A', 'C']);
  app.deliver({
    jsonrpc: '2.0',
    id: correctCall.id,
    result: {
      structuredContent: {
        contractVersion: '1.0', challengeId: 'challenge-1', isCorrect: true,
        halted: false, canRetry: false, attempt: 2, state: 'completed',
        selectedChoiceIds: ['A', 'C'],
        feedback: [{ choiceId: 'A', message: 'This preserves validation.' }]
      }
    }
  });
  await new Promise(resolve => setImmediate(resolve));

  assert.equal(app.document.getElementById('note-section').hidden, false);
  assert.match(app.document.getElementById('status').textContent, /Correct/);
  const note = app.document.getElementById('note-statement');
  note.value = 'Keep validation at the boundary.';
  app.document.getElementById('note-kind').value = 'constraint';
  app.document.getElementById('note-form').dispatchEvent(
    new app.dom.window.Event('submit', { bubbles: true, cancelable: true }));
  await new Promise(resolve => setImmediate(resolve));
  const noteCall = app.messages.find(message =>
    message.method === 'tools/call' && message.params?.name === 'record_change_context_challenge_note');
  assert.deepEqual({ ...noteCall.params.arguments }, {
    challengeId: 'challenge-1',
    statement: 'Keep validation at the boundary.',
    contextKind: 'constraint'
  });
  app.deliver({
    jsonrpc: '2.0',
    id: noteCall.id,
    result: {
      structuredContent: {
        contractVersion: '1.0', challengeId: 'challenge-1', contextId: 'context-1',
        nodeId: 'Example.Service.Save()', contextKind: 'constraint',
        provenance: 'user-stated', status: 'recorded-unverified'
      }
    }
  });
  await new Promise(resolve => setImmediate(resolve));
  assert.match(app.document.getElementById('note-status').textContent, /Saved as constraint/);
  app.dom.window.close();
});

test('change-context challenge uses radios when exactly one answer is correct', async () => {
  const app = await loadApp('change-context-challenge.html');
  app.deliver({
    jsonrpc: '2.0',
    method: 'ui/notifications/tool-result',
    params: {
      structuredContent: {
        contractVersion: '1.0', challengeId: 'challenge-2', nodeId: 'node',
        question: 'Choose one.', requiredSelectionCount: 1,
        choices: [
          { id: 'A', code: 'A();' },
          { id: 'B', code: 'B();' },
          { id: 'C', code: 'C();' }
        ],
        attempt: 0, state: 'awaiting-answer', expiresAt: '2026-08-13T12:00:00Z',
        trustNotice: 'Verify the choices.'
      }
    }
  });

  const inputs = app.document.querySelectorAll('input[name="answer"]');
  assert.equal(inputs.length, 3);
  assert.ok(Array.from(inputs).every(input => input.type === 'radio'));
  app.dom.window.close();
});
