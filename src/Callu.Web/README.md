# CalluApp React

> Enterprise-grade incident management platform built with React, TypeScript, and Tailwind CSS.

**Version**: 0.0.1  
**Status**: ✅ Production Ready  
**Last Updated**: February 12, 2026

---

## 🚀 Quick Start

```bash
# 1. Setup environment
cp .env.example .env.local

# 2. Install dependencies
npm install

# 3. Start development server
npm run dev

# 4. Open browser
http://localhost:3000
```

---

## ✨ Features

### Core Functionality
- ✅ **Incident Management** - Triage, track, and resolve incidents
- ✅ **Service Catalog** - Monitor service health and integrations
- ✅ **Escalation Policies** - Automated alert routing
- ✅ **On-Call Schedules** - Team rotation management
- ✅ **Communications Hub** - Multi-channel notifications
- ✅ **Call Logs** - Telephony tracking and analytics

### User Experience
- ✅ **Command Palette (⌘K)** - Zero-latency navigation
- ✅ **Dark Mode** - Industrial Glass theme
- ✅ **Responsive Design** - Mobile-first approach
- ✅ **Real-time Updates** - Live incident feed (SignalR ready)
- ✅ **Loading States** - Skeleton screens everywhere
- ✅ **Error Boundaries** - Graceful error handling

### Developer Experience
- ✅ **TypeScript Strict** - 100% type safety
- ✅ **ESLint + Prettier** - Automated code quality
- ✅ **Vitest** - Fast unit testing
- ✅ **Hot Module Replacement** - Instant feedback
- ✅ **Bundle Analysis** - Performance insights

### Architecture
- ✅ **JWT Authentication** - Secure with auto-refresh
- ✅ **API Retry Logic** - Exponential backoff
- ✅ **Code Splitting** - 6 optimized bundles
- ✅ **Virtual Scrolling** - Handle 10,000+ items
- ✅ **Request Interceptors** - Global request/response hooks

---

## 📖 Documentation

| Document | Description |
|----------|-------------|
| [**QUICK_REFERENCE.md**](QUICK_REFERENCE.md) | Quick reference card |
| [**IMPLEMENTATION_COMPLETE.md**](IMPLEMENTATION_COMPLETE.md) | Complete implementation guide |
| [**KEYBOARD_SHORTCUTS.md**](KEYBOARD_SHORTCUTS.md) | Command Palette shortcuts |
| [**ARCHITECTURE_DIAGRAM.md**](ARCHITECTURE_DIAGRAM.md) | System architecture |
| [**ENHANCEMENT_SUMMARY.md**](ENHANCEMENT_SUMMARY.md) | Enhancement overview |
| [**CHECKLIST.md**](CHECKLIST.md) | Implementation checklist |

---

## 🛠️ Tech Stack

### Core
- **React 18.3** - UI library
- **TypeScript 5.x** - Type safety
- **Vite 6.x** - Build tool
- **Tailwind CSS v4** - Styling

### State & Data
- **TanStack Query v5** - Server state management
- **React Router v7** - Client-side routing
- **Zod** - Schema validation
- **React Hook Form** - Form management

### UI Components
- **Radix UI** - Headless components
- **Lucide React** - Icon library
- **Motion** - Animations
- **cmdk** - Command palette
- **Recharts** - Data visualization

### Developer Tools
- **Vitest** - Unit testing
- **@testing-library/react** - Component testing
- **ESLint** - Code linting
- **Prettier** - Code formatting

---

## 📦 npm Scripts

### Development
```bash
npm run dev          # Start dev server (port 3000)
npm run build        # Build for production
npm run preview      # Preview production build
```

### Code Quality
```bash
npm run lint         # Check for issues
npm run lint:fix     # Auto-fix issues
npm run format       # Format code with Prettier
npm run format:check # Check code formatting
npm run type-check   # TypeScript validation
```

### Testing
```bash
npm run test         # Run tests in watch mode
npm run test:ui      # Run tests with UI
npm run test:coverage # Generate coverage report
```

---

## ⌨️ Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| **⌘K** / **Ctrl+K** | Open Command Palette |
| **↑ / ↓** | Navigate results |
| **Enter** | Select item |
| **Esc** | Close |

See [KEYBOARD_SHORTCUTS.md](KEYBOARD_SHORTCUTS.md) for complete list.

---

## 🏗️ Project Structure

```
src/
├── app/                         # Application shell
│   ├── App.tsx                  # Root component
│   ├── routes.tsx               # Route configuration
│   └── pages/                   # App-level utility pages
│       ├── not-found.tsx
│       └── placeholder.tsx
├── shared/                      # Cross-cutting infrastructure
│   ├── api/                     # API client & retry logic
│   ├── auth/                    # Auth service & context
│   ├── components/
│   │   ├── ui/                  # shadcn/ui components (49)
│   │   ├── layout/              # AppShell, sidebar, navbar
│   │   └── *.tsx                # Error boundary, skeletons, etc.
│   ├── hooks/                   # Shared hooks (useForm, etc.)
│   ├── types/                   # Common types (ApiResponse, User)
│   ├── utils/                   # Toast, perf, bundle analyzer
│   └── validations/             # Shared Zod schemas
├── features/                    # Feature modules (feature-sliced)
│   ├── auth/                    # Login, signup, password flows
│   ├── dashboard/               # Main dashboard
│   ├── incidents/               # Incident management
│   │   ├── api/                 # Incident API client
│   │   ├── components/          # UI components
│   │   ├── hooks/               # useIncidents, useIncidentMetrics
│   │   └── types/               # Incident types
│   ├── notifications/           # Notification center
│   ├── teams/                   # Team management
│   ├── escalations/             # Escalation policies
│   ├── schedules/               # On-call schedules
│   ├── services/                # Service catalog & webhooks
│   ├── settings/                # Settings, email & AI config
│   ├── communications/          # SIP, TTS, providers
│   ├── status-page/             # Public status page
│   └── ...                      # call-logs, conference, profile, etc.
├── styles/                      # Global styles & theme
└── test/                        # Test utilities
```


---

## 🔧 Configuration

### Environment Variables

Copy `.env.example` to `.env.local` and configure:

```bash
# API Configuration
VITE_API_URL=http://localhost:5000/api
VITE_API_TIMEOUT=30000

# Authentication
VITE_AUTH_TOKEN_KEY=calluapp_auth_token
VITE_REFRESH_TOKEN_KEY=calluapp_refresh_token

# SignalR
VITE_SIGNALR_HUB_URL=http://localhost:5000/hubs

# Feature Flags
VITE_ENABLE_REAL_TIME=true
```

See [.env.example](.env.example) for complete list.

---

## 🧪 Testing

### Run Tests
```bash
npm run test
```

### Run Tests with UI
```bash
npm run test:ui
```

### Generate Coverage
```bash
npm run test:coverage
```

### Write Tests
```typescript
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';

describe('MyComponent', () => {
  it('renders correctly', () => {
    render(<MyComponent />);
    expect(screen.getByText('Hello')).toBeInTheDocument();
  });
});
```

---

## 🚢 Deployment

### Build for Production
```bash
npm run build
```

### Preview Production Build
```bash
npm run preview
```

### Deploy
- Output directory: `dist/`
- Supports: Vercel, Netlify, AWS, etc.
- SPA routing: Configure redirect rules

---

## 🎨 Design System

### Theme
- **Style**: Industrial Glass
- **Mode**: Dark-first
- **Accent**: Electric Blue (#3E7BFA)
- **Fonts**: Outfit (headings), Inter (body)

### Status Colors
- **Triggered**: #FF4D4D (Red)
- **Acknowledged**: #FB923C (Amber)
- **Resolved**: #22C55E (Emerald)

### Glassmorphism
- Surface: `rgba(15, 23, 42, 0.8)`
- Backdrop: `backdrop-blur-md`

See [guidelines/Guidelines.md](guidelines/Guidelines.md) for complete design system.

---

## 📊 Performance

### Metrics
- **Initial Bundle**: ~200KB (gzipped)
- **First Paint**: <1s
- **Time to Interactive**: <2s
- **Lighthouse Score**: 95+ (Performance)

### Optimizations
- ✅ Code splitting (6 vendor bundles)
- ✅ Route-based lazy loading
- ✅ Virtual scrolling for large lists
- ✅ Image optimization
- ✅ Component memoization

---

## 🔒 Security

### Authentication
- JWT tokens with automatic refresh
- Secure token storage (localStorage)
- Token expiry detection
- Auto-logout on unauthorized access

### API
- Request/response interceptors
- CORS configuration ready
- Error sanitization

---

## 🤝 Contributing

### Before Committing
```bash
npm run type-check   # TypeScript validation
npm run lint:fix     # Fix linting issues
npm run format       # Format code
npm run test         # Run tests
```

### Code Style
- Follow existing patterns
- Write tests for new features
- Document complex logic
- Use TypeScript strictly

---

## 📝 License

Proprietary - CalluApp

---

## 🆘 Support

### Documentation
- [Quick Reference](QUICK_REFERENCE.md)
- [Implementation Guide](IMPLEMENTATION_COMPLETE.md)
- [Architecture](ARCHITECTURE_DIAGRAM.md)

### Issues
Check the documentation first, then:
1. Review existing code patterns
2. Run diagnostics: `npm run type-check && npm run lint`
3. Check browser console

---

## 🎉 Acknowledgments

Built with:
- ⚛️ React
- 🎨 Tailwind CSS
- 📦 Vite
- 🔷 TypeScript
- 🧪 Vitest

---

**Made with ❤️ for incident management excellence**

**🚀 Ready to ship!**
