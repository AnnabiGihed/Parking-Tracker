// Finds the adb program and checks the APK paths the tests are given, so that no program is looked up through PATH
// and nothing taken from the command line can be read by adb as an option.
import { existsSync, statSync } from 'node:fs';
import { isAbsolute, join, resolve } from 'node:path';

// adb from the Android SDK the CI runner installs (ANDROID_HOME, or ANDROID_SDK_ROOT on older images).
export function adbPath() {
  const sdk = process.env.ANDROID_HOME || process.env.ANDROID_SDK_ROOT;
  if (!sdk || !isAbsolute(sdk)) {
    throw new Error('ANDROID_HOME must be set to the absolute path of the Android SDK.');
  }
  const adb = join(sdk, 'platform-tools', process.platform === 'win32' ? 'adb.exe' : 'adb');
  if (!existsSync(adb)) {
    throw new Error(`adb not found at ${adb}.`);
  }
  return adb;
}

// An APK path from the command line, as an absolute path to an existing .apk file; anything else stops the test.
export function apkArgument(value, name) {
  if (typeof value !== 'string' || !value.toLowerCase().endsWith('.apk')) {
    throw new Error(`${name} must be the path of an .apk file.`);
  }
  const path = resolve(value);
  if (!existsSync(path) || !statSync(path).isFile()) {
    throw new Error(`${name} does not exist: ${path}`);
  }
  return path;
}
