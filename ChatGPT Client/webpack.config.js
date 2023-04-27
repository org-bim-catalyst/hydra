"use strict";

const path = require('path');
const webpack = require("webpack");
const NodePolyfillPlugin = require("node-polyfill-webpack-plugin")
const WebpackNotifierPlugin = require("webpack-notifier");

var BrowserSyncPlugin = require("browser-sync-webpack-plugin");

module.exports = {
    externals: {
        three: 'THREE',
        jquery: 'jQuery',
        bootstrap: 'bootstrap'
    },
    entry: {
        app_open_ai: './Scripts/ts/index.js',
        app_qr_code: './Scripts/ts/core/qr-generator.ts'
    },
    output: {
        path: path.resolve(__dirname, 'wwwroot/js'),
        filename: '[name].js',
        // `library` determines the name of the global variable
        library: {
            name: ['app_open_ai'],
            type: 'var'
        },
        sourceMapFilename: "[name].js.map"
    },
    module: {
        rules: [
            {
                test: /\.js$/,
                enforce: "pre",
                use: ["source-map-loader"],
            },
            {
                test: /\.(ts|js)x?$/,
                exclude: /node_modules/,
                use: {
                    loader: "babel-loader",
                }
            }, {
                test: /\.(scss|css)$/,
                use: [
                    {
                        loader: 'style-loader'
                    },
                    {
                        loader: 'css-loader'
                    },
                    {
                        loader: 'postcss-loader',
                        options: {
                            postcssOptions: {
                                plugins: () => [
                                    require('autoprefixer')
                                ]
                            }
                        }
                    },
                    {
                        loader: 'sass-loader'
                    }
                ]
            }]
    },
    resolve: {
        fallback: {
            child_process: false,
            fs: false,
            path: false
        },
        extensions: ['.js', '.jsx', '.tsx', '.ts', '.json', '.wasm']
    },
    mode: "development",
    devtool: "source-map",

    plugins: [
        new webpack.DefinePlugin({
            "require.specified": "require.resolve"
        }),
        new WebpackNotifierPlugin(),
        new BrowserSyncPlugin()],
};