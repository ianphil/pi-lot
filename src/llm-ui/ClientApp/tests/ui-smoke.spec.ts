import { expect, test } from '@playwright/test'

const modelResponse = {
  defaultModel: 'gpt-5.4',
  models: [
    { id: 'gpt-5.4', displayName: 'gpt-5.4' },
    { id: 'claude-haiku-4.5', displayName: 'Claude Haiku 4.5' },
  ],
}

test.beforeEach(async ({ page }) => {
  await page.route('/api/models', async (route) => {
    await route.fulfill({ json: modelResponse })
  })
})

test('loads the editable chat shell', async ({ page }) => {
  await page.goto('/')

  await expect(page.getByRole('button', { name: 'Edit' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Preview' })).toBeVisible()
  await expect(page.getByTestId('conversation-editor')).toBeVisible()
  await expect(page.getByLabel('Model')).toHaveValue('gpt-5.4')
  await expect(page.getByLabel('Message')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Send' })).toBeDisabled()
})

test('renders markdown preview and preserves edited source', async ({ page }) => {
  await page.goto('/')

  await replaceEditorText(page, '## User\n\nHello **world**')
  await page.getByRole('button', { name: 'Preview' }).click()

  await expect(page.getByTestId('markdown-preview').getByRole('heading', { name: 'User' })).toBeVisible()
  await expect(page.getByTestId('markdown-preview').getByText('Hello world')).toBeVisible()

  await page.getByRole('button', { name: 'Edit' }).click()
  await expect(page.locator('.cm-content')).toContainText('Hello **world**')
})

test('loads available models and keeps gpt-5.4 as the default', async ({ page }) => {
  await page.goto('/')

  const modelPicker = page.getByLabel('Model')
  await expect(modelPicker).toHaveValue('gpt-5.4')
  await expect(modelPicker.locator('option')).toHaveCount(2)
  await modelPicker.selectOption('claude-haiku-4.5')
  await expect(modelPicker).toHaveValue('claude-haiku-4.5')
})

test('streams assistant output into the markdown conversation', async ({ page }) => {
  await page.route('/api/chat', async (route) => {
    await route.fulfill({
      headers: { 'content-type': 'text/event-stream' },
      body:
        'data: {"type":"delta","text":"pong"}\n\n' +
        'data: {"type":"done"}\n\n',
    })
  })
  await page.goto('/')

  await page.getByLabel('Message').fill('Ping')
  await page.getByRole('button', { name: 'Send' }).click()

  await expect(page.locator('.cm-content')).toContainText('## User')
  await expect(page.locator('.cm-content')).toContainText('Ping')
  await expect(page.locator('.cm-content')).toContainText('## Assistant')
  await expect(page.locator('.cm-content')).toContainText('pong')
  await expect(page.getByRole('status')).toContainText('Ready')
})

test('shows streamed context budget warnings', async ({ page }) => {
  await page.route('/api/chat', async (route) => {
    await route.fulfill({
      headers: { 'content-type': 'text/event-stream' },
      body:
        'data: {"type":"warning","message":"Context estimate is 60% of budget.","estimatedTokens":60,"budgetTokens":100,"usageRatio":0.6}\n\n' +
        'data: {"type":"done"}\n\n',
    })
  })
  await page.goto('/')

  await page.getByLabel('Message').fill('Ping')
  await page.getByRole('button', { name: 'Send' }).click()

  await expect(page.getByRole('alert')).toContainText('Context estimate is 60% of budget.')
  await expect(page.getByRole('status')).toContainText('Ready')
})

test('shows a validation failure without losing the editable transcript', async ({ page }) => {
  await page.route('/api/chat', async (route) => {
    await route.fulfill({
      status: 400,
      json: { errors: ['Tool sections are not supported in the first llm-ui prototype.'] },
    })
  })
  await page.goto('/')

  await replaceEditorText(page, '## Tool\n\ntool output')
  await page.getByLabel('Message').fill('Run this')
  await page.getByRole('button', { name: 'Send' }).click()

  await expect(page.getByRole('status')).toContainText('Chat request failed with 400')
  await expect(page.locator('.cm-content')).toContainText('## Tool')
  await expect(page.locator('.cm-content')).toContainText('tool output')
})

async function replaceEditorText(page: import('@playwright/test').Page, text: string) {
  await page.locator('.cm-content').click()
  const modifier = process.platform === 'darwin' ? 'Meta' : 'Control'
  await page.keyboard.press(`${modifier}+A`)
  await page.keyboard.type(text)
}
