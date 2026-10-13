import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// The design's fonts (Stitch "Obsidian Telemetry"), served by the site itself: Inter for text, JetBrains Mono for
// numbers. Only the latin subsets, the weights in use.
import '@fontsource-variable/inter/wght.css'
import '@fontsource/jetbrains-mono/latin-400.css'
import '@fontsource/jetbrains-mono/latin-500.css'
import '@fontsource/jetbrains-mono/latin-700.css'
import './index.css'
import App from './App.jsx'

createRoot(document.getElementById('root')).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
