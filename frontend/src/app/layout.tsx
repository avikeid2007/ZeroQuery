import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "ZeroQuery Desktop",
  description: "Point at a database, get an app.",
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="en" suppressHydrationWarning className="h-full antialiased">
      <head>
        <link rel="preconnect" href="https://fonts.googleapis.com" />
        <link rel="preconnect" href="https://fonts.gstatic.com" crossOrigin="anonymous" />
        <link
          href="https://fonts.googleapis.com/css2?family=Segoe+UI+Variable+Text:wght@400;500;600;700&family=IBM+Plex+Mono:wght@400;500;600&display=swap"
          rel="stylesheet"
        />
        <script
          dangerouslySetInnerHTML={{
            __html: `
              try {
                const saved = localStorage.getItem('zq_theme');
                const prefersLight = window.matchMedia('(prefers-color-scheme: light)').matches;
                const theme = saved === 'light' || (!saved && prefersLight) ? 'light' : 'dark';
                document.documentElement.setAttribute('data-theme', theme);
                document.documentElement.classList.add(theme);
              } catch (_) {}
            `,
          }}
        />
      </head>
      <body className="h-screen w-screen overflow-hidden flex flex-col">{children}</body>
    </html>
  );
}
