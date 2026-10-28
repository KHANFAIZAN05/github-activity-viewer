import { useEffect, useState, type ReactNode } from 'react'
import { Moon, Sun } from 'lucide-react'

export type Theme = 'light' | 'dark'

const STORAGE_KEY = 'ghstory-theme'
const themes: { id: Theme; label: string; icon: ReactNode }[] = [
  { id: 'light', label: 'Light', icon: <Sun size={15} /> },
  { id: 'dark', label: 'Dark', icon: <Moon size={15} /> },
]

function initialTheme(): Theme {
  const stored = localStorage.getItem(STORAGE_KEY)
  if (stored === 'light' || stored === 'dark') return stored
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

export function useTheme() {
  const [theme, setTheme] = useState<Theme>(initialTheme)

  useEffect(() => {
    document.documentElement.dataset.theme = theme
    localStorage.setItem(STORAGE_KEY, theme)
  }, [theme])

  return [theme, setTheme] as const
}

export function ThemeSwitch({ theme, onChange }: { theme: Theme; onChange: (theme: Theme) => void }) {
  return (
    <div className="theme-switch" role="group" aria-label="Colour theme">
      {themes.map((item) => (
        <button
          key={item.id}
          aria-pressed={theme === item.id}
          title={`${item.label} theme`}
          onClick={() => onChange(item.id)}
        >
          <span aria-hidden="true">{item.icon}</span>
          <span className="label">{item.label}</span>
        </button>
      ))}
    </div>
  )
}
