import java.util.Properties

plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
}

// umbral.properties is the one place to configure the sample.
val umbral = Properties().apply {
    rootProject.file("umbral.properties").inputStream().use { load(it) }
}

// A BuildConfig String field is Java source: quote and escape the value.
fun buildConfigString(key: String): String {
    val value = umbral.getProperty(key, "").trim()
    return "\"" + value.replace("\\", "\\\\").replace("\"", "\\\"") + "\""
}

android {
    namespace = "com.cosmicchimps.umbral.sample"
    compileSdk = 37

    defaultConfig {
        applicationId = "com.cosmicchimps.umbral.sample"
        minSdk = 26
        targetSdk = 37
        versionCode = 1
        versionName = "1.0"

        buildConfigField("String", "UMBRAL_ISSUER", buildConfigString("UMBRAL_ISSUER"))
        buildConfigField("String", "UMBRAL_CLIENT_ID", buildConfigString("UMBRAL_CLIENT_ID"))

        // AppAuth's own RedirectUriReceiverActivity listens on this scheme.
        manifestPlaceholders["appAuthRedirectScheme"] = "com.cosmicchimps.umbral.sample"
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
}

kotlin {
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
    }
}

dependencies {
    implementation(platform(libs.compose.bom))
    implementation(libs.compose.ui)
    implementation(libs.compose.material3)
    implementation(libs.androidx.activity.compose)
    implementation(libs.appauth)
}
