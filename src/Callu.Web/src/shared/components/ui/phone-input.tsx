import React from "react";
import PhoneInputFromLib from "react-phone-number-input";
import "react-phone-number-input/style.css";
import { cn } from "./utils";

export interface PhoneInputProps extends Omit<React.ComponentProps<"input">, "onChange" | "value"> {
  value?: string;
  onChange?: (value: string | undefined) => void;
  error?: string;
  className?: string;
  ref?: React.Ref<HTMLInputElement>;
}

function PhoneInput({ className, value, onChange, error, ref, ...props }: PhoneInputProps) {
  return (
    <div className={cn("relative w-full", className)}>
      <PhoneInputFromLib
        international
        defaultCountry="TR"
        value={value}
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        onChange={onChange as any}
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        ref={ref as any}
        className="flex gap-2 items-center"
        numberInputProps={{
          className: cn(
            "flex h-9 w-full rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-sm transition-colors file:border-0 file:bg-transparent file:text-sm file:font-medium placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring disabled:cursor-not-allowed disabled:opacity-50",
            error && "border-destructive focus-visible:ring-destructive"
          ),
          ...props,
        }}
      />
      <style>{`
        .PhoneInput {
          display: flex;
          align-items: center;
        }
        
        .PhoneInputCountry {
          margin-right: 0.5rem;
          display: flex;
          align-items: center;
        }
        
        .PhoneInputCountrySelect {
          background-color: transparent;
          color: inherit;
          border-radius: 0.375rem;
          opacity: 0;
          position: absolute;
          top: 0;
          left: 0;
          height: 100%;
          width: 100%;
          z-index: 1;
          cursor: pointer;
          appearance: none;
        }

        .PhoneInputCountryIcon {
          width: 1.5rem;
          height: 1rem;
          box-shadow: 0 0 0 1px var(--border);
          border-radius: 2px;
          overflow: hidden;
          background-color: var(--background);
          position: relative;
        }

        .PhoneInputCountryIconImg {
          display: block;
          width: 100%;
          height: 100%;
          object-fit: cover;
        }
        
        .PhoneInputCountrySelectArrow {
          display: none;
        }
        
        .PhoneInputCountryIcon--border {
           box-shadow: 0 0 0 1px var(--border);
           background-color: var(--muted);
        }
        
        .PhoneInputInput {
           flex: 1;
           min-width: 0;
           background-color: transparent;
        }
        
        :root.dark .PhoneInputCountryIcon {
           box-shadow: 0 0 0 1px var(--border);
        }
        
        input.PhoneInputInput {
           background-color: transparent !important;
        }
      `}</style>
    </div>
  );
}

export { PhoneInput };
export default PhoneInput;
