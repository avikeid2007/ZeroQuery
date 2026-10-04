"use client";

import React from "react";

interface ZeroQueryLogoProps {
  size?: number;
  showText?: boolean;
  showBadge?: boolean;
  className?: string;
}

export default function ZeroQueryLogo({
  size = 24,
  showText = false,
  showBadge = false,
  className = "",
}: ZeroQueryLogoProps) {
  const iconSize = size;

  return (
    <div className={`inline-flex items-center gap-2.5 select-none ${className}`}>
      {/* Precision Vector Mark */}
      <div
        style={{ width: iconSize, height: iconSize }}
        className="relative shrink-0 flex items-center justify-center"
      >
        <svg
          viewBox="0 0 36 36"
          width={iconSize}
          height={iconSize}
          fill="none"
          xmlns="http://www.w3.org/2000/svg"
          className="transition-transform duration-200 hover:scale-105"
        >
          <defs>
            {/* Main Brand Gradient: Emerald Teal -> Electric Cyan -> Deep Blue */}
            <linearGradient id="zqLogoGrad" x1="0%" y1="0%" x2="100%" y2="100%">
              <stop offset="0%" stopColor="#4FB8A0" />
              <stop offset="55%" stopColor="#38BDF8" />
              <stop offset="100%" stopColor="#2563EB" />
            </linearGradient>

            {/* Accent Highlight Gradient */}
            <linearGradient id="zqHighlightGrad" x1="0%" y1="0%" x2="100%" y2="0%">
              <stop offset="0%" stopColor="#A7F3D0" />
              <stop offset="100%" stopColor="#38BDF8" />
            </linearGradient>

            {/* Container Subtle Shadow */}
            <filter id="zqShadow" x="-15%" y="-15%" width="130%" height="130%">
              <feDropShadow dx="0" dy="2" stdDeviation="2.5" floodColor="#4FB8A0" floodOpacity="0.25" />
            </filter>
          </defs>

          {/* Squircle App Container */}
          <rect
            x="1"
            y="1"
            width="34"
            height="34"
            rx="8.5"
            fill="url(#zqLogoGrad)"
            filter="url(#zqShadow)"
          />

          {/* Subtle Inner Glass Bevel */}
          <rect
            x="1.5"
            y="1.5"
            width="33"
            height="33"
            rx="8"
            stroke="white"
            strokeWidth="0.8"
            strokeOpacity="0.3"
            fill="none"
          />

          {/* Geometric "Z" Bolt / Query Spark Vector */}
          {/* Top Bar of Z */}
          <path
            d="M 9.5 10.5 H 25 C 26.2 10.5 26.8 11.8 25.8 12.8 L 19.5 19.5 L 25.5 19.5 C 26.3 19.5 26.8 20.2 26.5 21 L 24 25.5 H 10 C 8.8 25.5 8.2 24.2 9.2 23.2 L 15.8 16.5 L 9.8 16.5 C 9 16.5 8.5 15.8 8.8 15 Z"
            fill="#FFFFFF"
            fillRule="evenodd"
          />

          {/* Core Energy Highlight */}
          <circle cx="24.5" cy="24.5" r="1.5" fill="#A7F3D0" />
        </svg>
      </div>

      {/* Optional Typography */}
      {showText && (
        <div className="flex items-center gap-1.5 leading-none">
          <span className="font-bold text-[14px] tracking-tight text-text">ZeroQuery</span>
          {showBadge && <span className="app-badge-desktop ml-1">Desktop</span>}
        </div>
      )}
    </div>
  );
}
