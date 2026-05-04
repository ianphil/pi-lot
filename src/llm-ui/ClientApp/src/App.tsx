import { markdown } from '@codemirror/lang-markdown'
import CodeMirror from '@uiw/react-codemirror'
import DOMPurify from 'dompurify'
import MarkdownIt from 'markdown-it'
import { useEffect, useMemo, useState } from 'react'
import './App.css'

type ViewMode = 'edit' | 'preview'

type ModelInfo = {
  id: string
  displayName: string
}

type ModelsResponse = {
  defaultModel: string
  models: ModelInfo[]
}

const initialMarkdown = `## System

You are a concise, practical engineering partner.

## User

Help me think through this editable-context chat experiment.
`

const markdownRenderer = new MarkdownIt({
  breaks: true,
  linkify: true,
  typographer: true,
})

function App() {
  const [mode, setMode] = useState<ViewMode>('edit')
  const [conversationMarkdown, setConversationMarkdown] = useState(initialMarkdown)
  const [message, setMessage] = useState('')
  const [models, setModels] = useState<ModelInfo[]>([])
  const [model, setModel] = useState('gpt-5.4')
  const [status, setStatus] = useState('Ready')
  const [isSending, setIsSending] = useState(false)

  useEffect(() => {
    const controller = new AbortController()

    async function loadModels() {
      const response = await fetch('/api/models', { signal: controller.signal })
      if (!response.ok) {
        throw new Error(`Model request failed with ${response.status}`)
      }

      const payload = (await response.json()) as ModelsResponse
      setModel(payload.defaultModel)
      setModels(payload.models)
    }

    loadModels().catch((error: unknown) => {
      if (!controller.signal.aborted) {
        setStatus(error instanceof Error ? error.message : 'Could not load models')
      }
    })

    return () => controller.abort()
  }, [])

  const previewHtml = useMemo(
    () => DOMPurify.sanitize(markdownRenderer.render(conversationMarkdown)),
    [conversationMarkdown],
  )

  async function sendMessage() {
    if (!message.trim() || isSending) {
      return
    }

    const userMessage = message.trim()
    const markdownWithMessage = appendSection(conversationMarkdown, 'User', userMessage)
    setConversationMarkdown(`${markdownWithMessage}\n\n## Assistant\n\n`)
    setMessage('')
    setStatus('Thinking...')
    setIsSending(true)

    try {
      const response = await fetch('/api/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          model,
          conversationMarkdown: markdownWithMessage,
          message: userMessage,
        }),
      })

      if (!response.ok || response.body === null) {
        throw new Error(`Chat request failed with ${response.status}`)
      }

      await readSseStream(response.body, (event) => {
        if (event.type === 'delta') {
          setConversationMarkdown((current) => current + event.text)
        }

        if (event.type === 'error') {
          throw new Error(event.message)
        }
      })

      setStatus('Ready')
    } catch (error: unknown) {
      setStatus(error instanceof Error ? error.message : 'Chat request failed')
    } finally {
      setIsSending(false)
    }
  }

  return (
    <main className="app-shell">
      <header className="toolbar" aria-label="Conversation mode">
        <button
          type="button"
          className={mode === 'edit' ? 'active' : ''}
          onClick={() => setMode('edit')}
        >
          Edit
        </button>
        <button
          type="button"
          className={mode === 'preview' ? 'active' : ''}
          onClick={() => setMode('preview')}
        >
          Preview
        </button>
      </header>

      <section className="conversation" aria-label="Conversation context">
        {mode === 'edit' ? (
          <div data-testid="conversation-editor">
            <CodeMirror
              value={conversationMarkdown}
              height="100%"
              extensions={[markdown()]}
              basicSetup={{ lineNumbers: false, foldGutter: false }}
              onChange={setConversationMarkdown}
            />
          </div>
        ) : (
          <article
            className="markdown-preview"
            data-testid="markdown-preview"
            dangerouslySetInnerHTML={{ __html: previewHtml }}
          />
        )}
      </section>

      <section className="composer" aria-label="Chat composer">
        <select
          aria-label="Model"
          value={model}
          onChange={(event) => setModel(event.target.value)}
        >
          {models.length === 0 ? (
            <option value={model}>{model}</option>
          ) : (
            models.map((item) => (
              <option key={item.id} value={item.id}>
                {item.displayName}
              </option>
            ))
          )}
        </select>
        <textarea
          aria-label="Message"
          placeholder="Send a message with the edited Markdown as context..."
          value={message}
          onChange={(event) => setMessage(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter' && (event.metaKey || event.ctrlKey)) {
              event.preventDefault()
              void sendMessage()
            }
          }}
        />
        <button type="button" disabled={isSending || !message.trim()} onClick={sendMessage}>
          Send
        </button>
      </section>

      <footer className="status" role="status">
        {status}
      </footer>
    </main>
  )
}

function appendSection(markdownText: string, heading: string, content: string) {
  const trimmed = markdownText.trimEnd()
  return `${trimmed}\n\n## ${heading}\n\n${content}`
}

type StreamEvent =
  | { type: 'delta'; text: string }
  | { type: 'done' }
  | { type: 'error'; message: string }

async function readSseStream(
  body: ReadableStream<Uint8Array>,
  onEvent: (event: StreamEvent) => void,
) {
  const reader = body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''

  while (true) {
    const { done, value } = await reader.read()
    if (done) {
      break
    }

    buffer += decoder.decode(value, { stream: true })
    const chunks = buffer.split('\n\n')
    buffer = chunks.pop() ?? ''

    for (const chunk of chunks) {
      const data = chunk
        .split('\n')
        .filter((line) => line.startsWith('data:'))
        .map((line) => line.slice(5).trimStart())
        .join('\n')

      if (data) {
        onEvent(JSON.parse(data) as StreamEvent)
      }
    }
  }
}

export default App
