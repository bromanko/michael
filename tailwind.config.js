/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    "./src/frontend/booking/src/**/*.elm",
    "./src/frontend/admin/src/**/*.elm",
    "./src/backend/wwwroot/index.html",
    "./src/backend/wwwroot/admin/index.html",
  ],
  theme: {
    extend: {
      colors: {
        sand: {
          50: "#FDFCF8",
          100: "#FAF9F0",
          200: "#F0EDE2",
          300: "#DDD9CB",
          400: "#B8B3A4",
          500: "#87867F",
          600: "#5E5D58",
          700: "#3D3D38",
          800: "#2A2A26",
          900: "#131314",
        },
        coral: {
          DEFAULT: "#D97757",
          light: "#E8956F",
          dark: "#C0613F",
        },
      },
      fontFamily: {
        sans: [
          '"Styrene A"',
          "system-ui",
          "-apple-system",
          "Segoe UI",
          "sans-serif",
        ],
        display: [
          '"Tiempos Headline"',
          "Georgia",
          '"Times New Roman"',
          "serif",
        ],
      },
      fontSize: {
        question: ["2rem", { lineHeight: "1.2", letterSpacing: "-0.01em" }],
        "question-lg": [
          "2.5rem",
          { lineHeight: "1.15", letterSpacing: "-0.02em" },
        ],
      },
    },
  },
  plugins: [],
};
