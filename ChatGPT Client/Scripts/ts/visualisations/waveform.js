"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
var d3 = require("d3");
var VizWaveform = /** @class */ (function () {
    function VizWaveform(_selector) {
        var svg = d3.select(_selector);
        svg.attr('background-color', 'white');
        svg.selectAll('*').remove();
        this.svg = svg;
    }
    VizWaveform.prototype.visualize = function (data) {
        // content area of your visualization
        var vis = this.svg.append('g');
        vis.append('path');
        var margin = {
            top: 0,
            right: 0,
            bottom: 0,
            left: 0
        };
        var width = +this.svg.attr('width') - margin.left - margin.right;
        var height = +this.svg.attr('height') - margin.top - margin.bottom;
        var numberOfPoints = Math.ceil(width / 2);
        var xScale = d3.scaleLinear()
            .range([0, width])
            .domain([0, numberOfPoints]);
        var yScale = d3.scaleLinear()
            .range([height, 0])
            .domain([-1, 1]);
        var line = d3.line()
            .x(function (d, i) { return xScale(i); })
            .y(function (d, i) { return yScale(d); });
        vis.select("path")
            .datum(this.subsample(data, numberOfPoints))
            .attr("d", line)
            .attr('stroke', '#9575CD')
            .attr('fill', 'none');
    };
    VizWaveform.prototype.subsample = function (data, numberOfPoints) {
        var subsampledData = new Float32Array(numberOfPoints);
        for (var i = 0; i < numberOfPoints; i++) {
            subsampledData[i] = data[Math.floor(i / numberOfPoints * data.length)];
        }
        console.log(subsampledData);
        return subsampledData;
    };
    return VizWaveform;
}());
exports.default = VizWaveform;
//# sourceMappingURL=waveform.js.map