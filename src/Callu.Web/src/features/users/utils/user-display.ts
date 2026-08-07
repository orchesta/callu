import type { UserDto } from "../types/user.types";

const avatarColors = [
  "bg-brand-500",
  "bg-purple-500",
  "bg-emerald-500",
  "bg-amber-500",
  "bg-rose-500",
  "bg-teal-500",
  "bg-indigo-500",
  "bg-cyan-500",
];

/** Same id always gets the same colour, so a user looks consistent across screens. */
export function getAvatarColor(id: string): string {
  let hash = 0;
  for (let i = 0; i < id.length; i++) hash = id.charCodeAt(i) + ((hash << 5) - hash);
  return avatarColors[Math.abs(hash) % avatarColors.length];
}

/** The first letter of each of the first two words of a display name. */
export function initialsFromName(name: string): string {
  return name
    .split(" ")
    .filter(Boolean)
    .map((word) => word[0])
    .join("")
    .slice(0, 2)
    .toUpperCase();
}

export function getUserInitials(user: UserDto): string {
  if (user.initials) return user.initials;
  const first = user.firstName?.charAt(0) || "";
  const last = user.lastName?.charAt(0) || "";
  if (first || last) return initialsFromName(`${first} ${last}`);
  return user.email.charAt(0).toUpperCase();
}

export function getUserFullName(user: UserDto): string {
  if (user.displayName) return user.displayName;
  if (user.firstName || user.lastName) return `${user.firstName ?? ""} ${user.lastName ?? ""}`.trim();
  return user.email;
}
