// Self-register a user through the forge's sign-up page (no email confirmation configured).
// node signup.mjs <base> <username> <email> <password>
import { chromium } from '/tmp/r3du-pw/node_modules/playwright/index.mjs';
const [base, username, email, password] = process.argv.slice(2);
const browser = await chromium.launch({ channel: 'chrome' });
const page = await browser.newPage();
await page.goto(base + '/user/sign_up');
await page.fill('#user_name', username);
await page.fill('#email', email);
await page.fill('#password', password);
await page.fill('#retype', password);
await page.click('form[action="/user/sign_up"] button.primary');
await page.waitForLoadState('load');
console.log('after sign-up:', page.url(), '|', (await page.locator('.ui.message, .flash-message, .ui.negative, .ui.positive').allInnerTexts()).join(' / ').replace(/\s+/g, ' ').slice(0, 300));
await browser.close();
