import { forwardRef, type InputHTMLAttributes } from 'react';

import { cn } from './utils';

export interface InputProps extends InputHTMLAttributes<HTMLInputElement> {
  invalid?: boolean;
}

export const Input = forwardRef<HTMLInputElement, InputProps>(function Input(
  { className, invalid = false, ...props },
  ref,
) {
  return (
    <input
      ref={ref}
      className={cn(
        'flex h-10 w-full rounded border bg-white px-3 py-2 text-sm',
        'placeholder:text-slate-400',
        'focus:outline-none focus:ring-2 focus:ring-brand-400',
        'disabled:cursor-not-allowed disabled:opacity-50',
        invalid ? 'border-red-500 focus:ring-red-400' : 'border-slate-300',
        className,
      )}
      {...props}
    />
  );
});
