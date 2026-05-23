import type { ReactNode } from 'react';

import { cn } from './utils';

export interface LayoutProps {
  sidebar: ReactNode;
  topbar: ReactNode;
  children: ReactNode;
  className?: string;
}

export function Layout({ sidebar, topbar, children, className }: LayoutProps) {
  return (
    <div className={cn('flex min-h-screen bg-slate-50', className)}>
      <aside className="w-64 shrink-0 border-r border-slate-200 bg-white">{sidebar}</aside>
      <div className="flex flex-1 flex-col">
        <header className="flex h-14 items-center border-b border-slate-200 bg-white px-6">
          {topbar}
        </header>
        <main className="flex-1 overflow-auto p-6">{children}</main>
      </div>
    </div>
  );
}
