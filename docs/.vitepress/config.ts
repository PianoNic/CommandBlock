import { defineConfig } from 'vitepress'

// Docs site for CommandBlock, built from the markdown in this folder. Served at the domain root on
// Cloudflare Pages, so no `base` is needed. Build: `vitepress build` (output: .vitepress/dist).
export default defineConfig({
  title: 'CommandBlock',
  description: 'Self-hosted Minecraft server manager. Spin up servers, browse modpacks, and route them all through one port by hostname.',
  lastUpdated: true,
  cleanUrls: true,
  // README-style links elsewhere point at "docs/*.md"; inside the site links resolve fine, but keep
  // the build from failing on the odd absolute/anchor link.
  ignoreDeadLinks: true,
  head: [
    ['link', { rel: 'icon', href: '/favicon.svg' }],
  ],
  themeConfig: {
    nav: [
      { text: 'Intro', link: '/intro' },
      { text: 'Setup', link: '/self-host' },
      { text: 'Servers', link: '/servers' },
      { text: 'Routing', link: '/routing' },
      { text: 'Development', link: '/dev-setup' },
    ],
    sidebar: [
      { text: 'What is CommandBlock?', link: '/intro' },
      {
        text: 'Setup',
        collapsed: false,
        items: [
          { text: 'Self-hosting', link: '/self-host' },
        ],
      },
      {
        text: 'Guides',
        collapsed: false,
        items: [
          { text: 'Servers & modpacks', link: '/servers' },
          { text: 'Backups', link: '/backups' },
          { text: 'Hostname routing', link: '/routing' },
        ],
      },
      {
        text: 'Development',
        collapsed: false,
        items: [
          { text: 'Developer setup', link: '/dev-setup' },
        ],
      },
    ],
    socialLinks: [
      { icon: 'github', link: 'https://github.com/PianoNic/CommandBlock' },
    ],
    search: { provider: 'local' },
    editLink: {
      pattern: 'https://github.com/PianoNic/CommandBlock/edit/main/docs/:path',
      text: 'Edit this page on GitHub',
    },
    footer: {
      message: 'Made with care by PianoNic.',
      copyright: 'CommandBlock',
    },
  },
})
