package com.efootball.tournament

import android.annotation.SuppressLint
import android.os.Bundle
import android.webkit.WebView
import android.webkit.WebViewClient
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.compose.BackHandler
import androidx.compose.runtime.*
import androidx.compose.ui.viewinterop.AndroidView

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            TournamentWebView()
        }
    }
}

@SuppressLint("SetJavaScriptEnabled")
@Composable
fun TournamentWebView() {
    var webView: WebView? = remember { null }

    BackHandler {
        if (webView?.canGoBack() == true) {
            webView?.goBack()
        }
    }

    AndroidView(factory = { context ->
        WebView(context).apply {
            webViewClient = WebViewClient()
            settings.javaScriptEnabled = true
            settings.domStorageEnabled = true
            settings.loadWithOverviewMode = true
            settings.useWideViewPort = true
            loadUrl("https://efootball-tournament-production.up.railway.app")
            webView = this
        }
    })
}