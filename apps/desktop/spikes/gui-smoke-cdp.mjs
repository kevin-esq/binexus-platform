/**
 * CDP UI driver for installed Binexus desktop (WebView2 remote debugging).
 * Does not call Tauri invoke from the console — only fills forms and clicks.
 * Usage: node gui-smoke-cdp.mjs <scenario>
 */
import { chromium } from 'playwright';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';

const CDP = process.env.BINEXUS_CDP_URL || 'http://127.0.0.1:9222';
const scenario = process.argv[2] || 'probe';

function hostInfo() {
  const p = path.join(os.tmpdir(), 'binexus-gui-smoke-host.json');
  return JSON.parse(fs.readFileSync(p, 'utf8'));
}

async function connect() {
  const browser = await chromium.connectOverCDP(CDP);
  const context = browser.contexts()[0] || (await browser.newContext());
  const page = context.pages()[0] || (await context.newPage());
  await page.waitForTimeout(1500);
  return { browser, page };
}

async function textOf(page) {
  return page.locator('main').innerText().catch(() => page.content());
}

async function scenarioServerSetup(page, url) {
  await page.waitForSelector('#branch-url', { timeout: 30000 });
  await page.fill('#branch-url', url);
  await page.click('button[type="submit"]');
  await page.waitForSelector('#pairing-payload', { timeout: 45000 });
  return textOf(page);
}

async function scenarioPair(page, payload, terminalName) {
  await page.waitForSelector('#pairing-payload', { timeout: 30000 });
  await page.fill('#pairing-payload', payload);
  await page.fill('#terminal-name', terminalName);
  await page.click('button[type="submit"]');
  await page.waitForTimeout(500);
  const remaining = await page.inputValue('#pairing-payload').catch(() => 'gone');
  await page.waitForSelector('text=Waiting for an administrator', { timeout: 60000 });
  const body = await textOf(page);
  return { remaining, body };
}

async function clickResume(page) {
  const btn = page.getByRole('button', { name: /Check approval|Resume|Continue/i });
  if (await btn.count()) {
    await btn.first().click();
  }
}

async function rejectUrl(page, url) {
  await page.waitForSelector('#branch-url', { timeout: 30000 });
  await page.fill('#branch-url', url);
  await page.click('button[type="submit"]');
  await page.waitForTimeout(1500);
  return textOf(page);
}

const { browser, page } = await connect();
const info = hostInfo();
let result = { scenario, ok: false };

try {
  switch (scenario) {
    case 'probe': {
      const body = await textOf(page);
      result = { scenario, ok: true, bodyPreview: body.slice(0, 400) };
      break;
    }
    case 'A-server': {
      const body = await scenarioServerSetup(page, info.baseUrl);
      result = {
        scenario,
        ok: /Pair this terminal|NeedsPairing|terminal/i.test(body),
        bodyPreview: body.slice(0, 500),
      };
      break;
    }
    case 'A-pair': {
      const payload = process.env.BINEXUS_SMOKE_PAIRING_PAYLOAD;
      if (!payload) throw new Error('BINEXUS_SMOKE_PAIRING_PAYLOAD required');
      const { remaining, body } = await scenarioPair(
        page,
        payload,
        process.env.BINEXUS_SMOKE_TERMINAL_NAME || 'GUI Smoke Terminal',
      );
      const codeCleared = remaining === '' || remaining === 'gone';
      result = {
        scenario,
        ok: codeCleared && /Waiting for an administrator/i.test(body),
        codeCleared,
        fingerprintVisible: /Device fingerprint:\s*[A-F0-9]{4}-[A-F0-9]{4}-[A-F0-9]{4}/i.test(body),
        fingerprintMatch: (body.match(/Device fingerprint:\s*([A-F0-9-]{14})/i) || [])[1] || null,
        bodyPreview: body.slice(0, 600),
      };
      break;
    }
    case 'A-resume': {
      await clickResume(page);
      await page.waitForTimeout(5000);
      const body = await textOf(page);
      result = {
        scenario,
        ok: /ready|Paired|Terminal paired/i.test(body),
        bodyPreview: body.slice(0, 600),
      };
      break;
    }
    case 'click-resume': {
      await clickResume(page);
      await page.waitForTimeout(3500);
      const body = await textOf(page);
      result = {
        scenario,
        ok: true,
        bodyPreview: body.slice(0, 800),
      };
      break;
    }
    case 'wait-paired': {
      for (let i = 0; i < 40; i++) {
        await clickResume(page).catch(() => {});
        const body = await textOf(page);
        if (/Terminal paired|This terminal is ready/i.test(body)) {
          result = { scenario, ok: true, bodyPreview: body.slice(0, 600) };
          break;
        }
        await page.waitForTimeout(1500);
      }
      if (!result.ok) {
        result = { scenario, ok: false, bodyPreview: (await textOf(page)).slice(0, 600) };
      }
      break;
    }
    case 'G-url': {
      const urls = (process.env.BINEXUS_SMOKE_BAD_URLS || '').split('|').filter(Boolean);
      const outcomes = [];
      for (const url of urls) {
        // May need to be on server setup; if paired, skip
        const body = await rejectUrl(page, url);
        outcomes.push({
          url: url.replace(/:\/\/.*@/, '://***@'),
          rejected: /not allowed|BRANCH_URL|Could not|error|invalid/i.test(body) &&
            !/Pair this terminal/i.test(body),
          preview: body.slice(0, 200),
        });
      }
      result = { scenario, ok: outcomes.every((o) => o.rejected), outcomes };
      break;
    }
    default:
      throw new Error(`Unknown scenario ${scenario}`);
  }
} catch (err) {
  result = { scenario, ok: false, error: String(err) };
}

const out = path.join(os.tmpdir(), `binexus-gui-smoke-${scenario}.json`);
fs.writeFileSync(out, JSON.stringify(result, null, 2));
console.log(JSON.stringify(result));
await browser.close().catch(() => {});
process.exit(result.ok ? 0 : 1);
