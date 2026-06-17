/**
 * Searchable IANA timezone picker. The @vvo/tzdb dataset is loaded on demand so
 * pages that don't use the picker don't pay the bundle cost.
 */

import * as React from "react";
import { Check, ChevronsUpDown, Globe } from "lucide-react";
import { cn } from "./ui/utils";
import { Button } from "@/shared/components/ui/button";
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from "@/shared/components/ui/command";
import { Popover, PopoverContent, PopoverTrigger } from "@/shared/components/ui/popover";

interface RawTimezone {
    name: string;
    alternativeName: string;
    group: string[];
    continentName: string;
    countryName: string;
    mainCities: string[];
    rawOffsetInMinutes: number;
    abbreviation: string;
    currentTimeFormat: string;
}

interface TimezoneOption {
    id: string;
    label: string;
    subtitle: string;
    offsetLabel: string;
}

async function loadTimezones(): Promise<TimezoneOption[]> {
    const mod = (await import("@vvo/tzdb")) as unknown as { getTimeZones: () => RawTimezone[] };
    const list = mod.getTimeZones();
    return list
        .sort((a, b) => a.rawOffsetInMinutes - b.rawOffsetInMinutes)
        .map((tz) => {
            const sign = tz.rawOffsetInMinutes >= 0 ? "+" : "-";
            const abs = Math.abs(tz.rawOffsetInMinutes);
            const hh = String(Math.floor(abs / 60)).padStart(2, "0");
            const mm = String(abs % 60).padStart(2, "0");
            return {
                id: tz.name,
                label: tz.mainCities[0] ?? tz.name,
                subtitle: `${tz.countryName} — ${tz.name}`,
                offsetLabel: `GMT${sign}${hh}:${mm}`,
            };
        });
}

export interface TimezonePickerProps {
    value: string;
    onChange: (timezone: string) => void;
    placeholder?: string;
    className?: string;
    disabled?: boolean;
}

export function TimezonePicker({ value, onChange, placeholder = "Select timezone", className, disabled }: TimezonePickerProps) {
    const [open, setOpen] = React.useState(false);
    const [options, setOptions] = React.useState<TimezoneOption[] | null>(null);

    React.useEffect(() => {
        if (open && options === null) {
            void loadTimezones().then(setOptions);
        }
    }, [open, options]);

    const current = options?.find((o) => o.id === value);

    return (
        <Popover open={open} onOpenChange={setOpen}>
            <PopoverTrigger asChild>
                <Button
                    type="button"
                    variant="outline"
                    role="combobox"
                    aria-expanded={open}
                    disabled={disabled}
                    className={cn("justify-between font-normal", className)}
                >
                    <span className="flex min-w-0 items-center gap-2">
                        <Globe className="h-4 w-4 flex-shrink-0 text-gray-400" />
                        <span className="truncate">{current?.label ?? value ?? placeholder}</span>
                    </span>
                    <ChevronsUpDown className="ml-2 h-4 w-4 flex-shrink-0 opacity-50" />
                </Button>
            </PopoverTrigger>
            <PopoverContent className="w-[360px] p-0" align="start">
                <Command>
                    <CommandInput placeholder="Search cities or IANA names..." />
                    <CommandList>
                        {options === null ? (
                            <div className="p-4 text-center text-sm text-gray-500">Loading timezones...</div>
                        ) : (
                            <>
                                <CommandEmpty>No timezone matched.</CommandEmpty>
                                <CommandGroup>
                                    {options.map((tz) => (
                                        <CommandItem
                                            key={tz.id}
                                            value={`${tz.id} ${tz.label} ${tz.subtitle}`}
                                            onSelect={() => {
                                                onChange(tz.id);
                                                setOpen(false);
                                            }}
                                        >
                                            <Check className={cn("mr-2 h-4 w-4", value === tz.id ? "opacity-100" : "opacity-0")} />
                                            <div className="flex min-w-0 flex-1 items-center justify-between gap-3">
                                                <div className="min-w-0">
                                                    <div className="truncate">{tz.label}</div>
                                                    <div className="truncate text-xs text-gray-500">{tz.subtitle}</div>
                                                </div>
                                                <span className="whitespace-nowrap font-mono text-xs text-gray-500">{tz.offsetLabel}</span>
                                            </div>
                                        </CommandItem>
                                    ))}
                                </CommandGroup>
                            </>
                        )}
                    </CommandList>
                </Command>
            </PopoverContent>
        </Popover>
    );
}
